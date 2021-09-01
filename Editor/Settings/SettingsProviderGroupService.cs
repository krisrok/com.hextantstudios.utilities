using UnityEngine;
using UnityEditor;
using System.Reflection;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Compilation;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Assembly = System.Reflection.Assembly;
using UnityAssembly = UnityEditor.Compilation.Assembly;

namespace Hextant.Editor
{

    public static class SettingsProviderGroupService
    {
        private class Cache
        {
            public bool NeedsFullScan;
            public AssemblyInfo[] AssemblyInfos;
            public string[] ChangedUnknownAssemblyLocations;

            public class AssemblyInfo
            {
                public string Location;
                public string[] TypeNames;
                public bool NeedsScan;

                public static AssemblyInfo FromScannedAssemblyInfo( ScannedAssemblyInfo sai )
                {
                    return new AssemblyInfo
                    {
                        Location = GetLocation( sai.Assembly ),
                        TypeNames = sai.Types
                            .Select( t => t.Type.FullName )
                            .ToArray()
                    };
                }
            }
        }

        private class ScannedAssemblyInfo
        {
            public Assembly Assembly;
            public ScannedTypeInfo[] Types;
        }

        private class ScannedTypeInfo
        {
            public Type Type;
            public Attribute Attribute;
            public PropertyInfo InstanceProp;
            public PropertyInfo DisplayPathProp;

            public ScannedTypeInfo( Type type )
            {
                Type = type;
                InstanceProp = type.GetProperty( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );
                DisplayPathProp =type.GetProperty( "displayPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );
            }
        }

        private class AssemblyLocation
        {
            public Assembly Assembly;
            public string Location;

            public static AssemblyLocation FromAssembly( Assembly assembly )
            {
                return new AssemblyLocation
                {
                    Assembly = assembly,
                    Location = GetLocation( assembly )
                };
            }

            public bool MatchesLocation(Cache.AssemblyInfo cachedAssemblyInfo)
            {
                return Location.EndsWith( cachedAssemblyInfo.Location );
            }
        }

        private static string GetLocation( Assembly assembly )
        {
            return assembly.Location.Replace( '\\', '/' );
        }

        private static bool _isInited;
        private static (TypeInfo Type, Attribute Attribute)[] _settingsTypes;

        private static string _projectDirPath => Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
        private static string _cacheFilePath => Path.GetFullPath( Path.Combine( Application.dataPath, "../Library/SettingsAssemblyCache" ) );

        static SettingsProviderGroupService()
        {
            CompilationPipeline.assemblyCompilationFinished += CompilationPipeline_assemblyCompilationFinished;
        }

        private static ScannedAssemblyInfo[] _assemblyInfos;

        private static ScannedAssemblyInfo[] GetAssemblyInfos()
        {
            if( _assemblyInfos != null )
                return _assemblyInfos;

            Debug.Log( $"{nameof( SettingsProviderGroupService )}.{nameof( GetAssemblyInfos )}" );
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var assemblyInfos =
                //TryReadCache( out var cache ) && cache.NeedsFullScan == false ?
                //PerformCachedScan( cache ) :
                PerformFullScan();

            _assemblyInfos = assemblyInfos.ToArray();

            WriteCache( new Cache
            {
                AssemblyInfos = assemblyInfos
                    .Select( sai => Cache.AssemblyInfo.FromScannedAssemblyInfo( sai ) )
                    .ToArray()
            } );

            //Debug.Log( $"Inspecting assemblies ({referencingAssemblies.Length}):\n" + string.Join( "\n", referencingAssemblies.Select( a => a.FullName ) ) );
            //Debug.Log( $"Found types ({_settingsTypes.Length}):\n" + string.Join( "\n", _settingsTypes.AsEnumerable() ) );

            Debug.Log( $"{nameof( SettingsProviderGroupService )}.{nameof( GetAssemblyInfos )} ran in {stopwatch.Elapsed.TotalSeconds}s" );

            return _assemblyInfos;
        }

        private static Assembly[] GetAllAssemblies()
        {
            // todo: most likely, we can filter out lots of those
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where( a => a.IsDynamic == false )
                .ToArray();
        }


        private static ScannedAssemblyInfo[] PerformCachedScan( Cache cache )
        {
            Debug.Log( "Doing a cached scan" );

            try
            {
                var allAssemblies = GetAllAssemblies();

                // cache the locations for easier comparability
                var assemblyLocations = allAssemblies
                    .Where( a => a.IsDynamic == false )
                    .Select( a => AssemblyLocation.FromAssembly(a) )
                    .ToArray();

                // filter out deleted/renamed/moved assemblies
                var filteredCachedAssemblyInfos = cache.AssemblyInfos?
                    .Where( ai => assemblyLocations.Any( al => al.MatchesLocation( ai ) ) )
                    .ToArray()
                    ?? new Cache.AssemblyInfo[ 0 ];

                // scan these
                var changedKnownAssemblyInfos = filteredCachedAssemblyInfos?
                    .Where( ra => ra.NeedsScan == true )
                    .Select( ai => ScanCached( assemblyLocations, ai ) )
                    ?? Enumerable.Empty<ScannedAssemblyInfo>();

                // use the cached types of these
                var unchangedKnownAssemblyInfos = filteredCachedAssemblyInfos?
                    .Where( ra => ra.NeedsScan == false )
                    .Select( ai => ScanCached( assemblyLocations, ai ) )
                    ?? Enumerable.Empty<ScannedAssemblyInfo>();

                // scan these too
                var otherChangedAssemblies = cache.ChangedUnknownAssemblyLocations?
                    .Select( al => assemblyLocations.FirstOrDefault( at => at.Location.EndsWith( al ) ).Assembly )
                    .Where( a => a != null )
                    .Select( a => ScanAssembly( a ) )
                    ?? Enumerable.Empty<ScannedAssemblyInfo>();

                return changedKnownAssemblyInfos
                    .Concat( unchangedKnownAssemblyInfos )
                    .Concat( otherChangedAssemblies )
                    .ToArray();
            }
            catch(Exception ex)
            {
                Debug.LogException( ex );
                return null;
            }
        }
        private static ScannedAssemblyInfo[] PerformFullScan( )
        {
            Debug.Log( "Doing a full scan" );

            var allAssemblies = GetAllAssemblies();
            var attributeAssemblyName = typeof( SettingsAttributeBase ).Assembly.FullName;

            // get a list of assemblies referencing our assembly
            var referencingAssemblies = allAssemblies
                .Where( a => a.GetReferencedAssemblies().Any( ra => ra.FullName == attributeAssemblyName ) );

            var scannedAssemblyInfos = referencingAssemblies
                .Select( a => ScanAssembly( a ) )
                .ToArray();

            return scannedAssemblyInfos;
        }

        private static void WriteCache( Cache cache )
        {
            File.WriteAllText( _cacheFilePath, JsonConvert.SerializeObject( cache ) );
        }

        private static void CompilationPipeline_assemblyCompilationFinished( string assemblyLocation, CompilerMessage[] arg2 )
        {
            Debug.Log( "Compilation finished: " + assemblyLocation );
            if( TryReadCache( out var cache ) )
            {
                var isDirty = false;
                Cache.AssemblyInfo cai;
                if( ( cai = cache.AssemblyInfos.FirstOrDefault( ai => ai.Location.EndsWith( assemblyLocation ) ) ) != null)
                {
                    Debug.Log( "REF!" );
                    cai.NeedsScan = true;
                    isDirty = true;
                }
                //else if( cache.NonReferencingAssemblies.Any( al => al.EndsWith( assemblyLocation ) ) )
                //    Debug.Log( "NONREF!" );
                else
                {
                    Debug.Log( "OTHER!" );
                    cache.ChangedUnknownAssemblyLocations = new[] { assemblyLocation };
                    isDirty = true;
                }

                if( isDirty )
                    WriteCache(cache);
            }

        }

        private static bool TryReadCache(out Cache cache)
        {
            try
            {
                cache = JsonConvert.DeserializeObject<Cache>( File.ReadAllText( _cacheFilePath ) );
                return true;
            }
            catch( Exception ex )
            {
                Debug.LogError( $"Could not read cache from: {_cacheFilePath}\n{ex}" );
                cache = default( Cache );
                return false;
            }
        }

        [SettingsProviderGroup]
        public static SettingsProvider[] CreateProviders()
        {
            var assemblyInfos = GetAssemblyInfos();

            var result = new List<SettingsProvider>();

            foreach( var at in assemblyInfos.SelectMany(ai => ai.Types) )
            {
                result.Add( new ScriptableObjectSettingsProvider( () => ( ScriptableObject )at.InstanceProp.GetValue( null ),
                    at.Attribute is EditorUserSettingsAttribute ?
                    SettingsScope.User : SettingsScope.Project,
                    ( string )at.DisplayPathProp.GetValue( null ) ) );
            }

            return result.ToArray();
        }

        private static ScannedAssemblyInfo ScanCached(AssemblyLocation[] assemblyLocations, Cache.AssemblyInfo assemblyInfo)
        {
            var assembly = assemblyLocations
                .FirstOrDefault( al => al.MatchesLocation( assemblyInfo ) )
                .Assembly;

            var types = assemblyInfo.TypeNames
                .Select( tn => assembly.GetType( tn ) )
                .Select( t => new ScannedTypeInfo(t) )
                .ToArray();

            return new ScannedAssemblyInfo
            {
                Assembly = assembly,
                Types = types
            };
        }

        private static ScannedAssemblyInfo ScanAssembly( Assembly assembly )
        {
            var typeAttributeTuples = assembly.DefinedTypes
                .Select( t => (Type: t, Attribute: t.GetCustomAttribute( typeof( SettingsAttributeBase ), true )) )
                .Where( ta => ta.Attribute != null );

            var types = new List<ScannedTypeInfo>();

            foreach(var ta in typeAttributeTuples)
            {
                if( ta.Type.GetMethods( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic ).Any( mi => mi.GetCustomAttribute<SettingsProviderAttribute>() != null ) )
                {
                    Debug.Log( $"SKIPPING {ta.Type}" );
                    continue;
                }

                if( typeof( ISettings ).IsAssignableFrom( ta.Type ) == false )
                {
                    Debug.LogError( $"{ta.Type} is decorated with {ta.Attribute.GetType()} but does not inherit from Settings<>!\nPlease remove the attribute or fix the inheritance to e.g. Settings<{ta.Type}>." );
                    continue;
                }

                Debug.Log( $"ADDING {ta.Type}" );

                types.Add( new ScannedTypeInfo(ta.Type) );
            }

            var result = new ScannedAssemblyInfo
            {
                Assembly = assembly,
                Types = types.ToArray()
            };

            return result;
        }
    }
}
