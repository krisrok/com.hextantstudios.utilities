using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Hextant
{
    public interface ISerializableSettings
    {
        void SaveAsJsonFile( string filename = null );
        void LoadFromJsonFile( string filename = null );
    }

    internal interface IOverridableSettings
    {
        internal List<string> overrideOriginFilePaths { get; set; }
        internal bool useOriginFileWatchers { get; set; }
    }

    public abstract class SerializableSettings<T> : Settings<T>, ISerializableSettings, IOverridableSettings where T : SerializableSettings<T>
    {
        private static List<FileSystemWatcher> _originFileWatchers;
        private static SynchronizationContext _syncContext;

        List<string> IOverridableSettings.overrideOriginFilePaths { get; set; }
        bool IOverridableSettings.useOriginFileWatchers { get; set; }

        protected override void InitializeInstance()
        {
            // Load runtime overrides from json file if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && ( attribute as IRuntimeSettingsAttribute ).allowRuntimeFileOverrides
#if UNITY_EDITOR
                          && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                LoadRuntimeFileOverrides();
        }

        internal static void LoadRuntimeFileOverrides()
        {
            var runtimeInstance = LoadOverridesFromAllFiles();

            if( runtimeInstance == null )
                return;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif

            var overridableSettings = ( IOverridableSettings )runtimeInstance;
            var overridesString = $"with overrides from file{( overridableSettings.overrideOriginFilePaths.Count > 1 ? "s" : "" )}: {string.Join( ", ", overridableSettings.overrideOriginFilePaths )}";
            Debug.Log( $"Created {typeof( T ).Name} runtime instance {overridesString}" );

            runtimeInstance.name = $"{instance.name} ({overridesString})";

            if( ( attribute as IRuntimeSettingsAttribute ).allowRuntimeFileWatchers )
                SetupOriginFileWatchers( overridableSettings );

            _instance = runtimeInstance;
        }

        #region filewatchers
        private static void SetupOriginFileWatchers( IOverridableSettings overridableSettings )
        {
            overridableSettings.useOriginFileWatchers = true;

            Application.onBeforeRender += Application_onBeforeRender;

            foreach( var originFilePath in overridableSettings.overrideOriginFilePaths )
            {
                var fsw = new FileSystemWatcher( Path.GetDirectoryName( Path.GetFullPath( originFilePath ) ), originFilePath );
                fsw.NotifyFilter = NotifyFilters.LastWrite;
                fsw.Changed += OverrideOriginFileChanged;
                fsw.EnableRaisingEvents = true;

                if( _originFileWatchers == null )
                    _originFileWatchers = new List<FileSystemWatcher>();
                _originFileWatchers.Add( fsw );
            }
        }

        private static void Application_onBeforeRender()
        {
            Application.onBeforeRender -= Application_onBeforeRender;
            _syncContext = SynchronizationContext.Current;
        }

        private static void ClearOriginFileWatchers()
        {
            if( _originFileWatchers == null )
                return;

            foreach( var fsw in _originFileWatchers )
            {
                fsw.EnableRaisingEvents = false;
                fsw.Changed -= OverrideOriginFileChanged;
            }

            _originFileWatchers = null;
        }

        private static void OverrideOriginFileChanged( object sender, FileSystemEventArgs e )
        {
            _syncContext.Post( _ =>
            {
                LoadOverridesFromAllFiles( _instance );

                var overridableSettings = ( IOverridableSettings )_instance;
                var overridesString = $"with overrides from file{( overridableSettings.overrideOriginFilePaths.Count > 1 ? "s" : "" )}: {string.Join( ", ", overridableSettings.overrideOriginFilePaths )}";
                Debug.Log( $"Updated {typeof( T ).Name} runtime instance {overridesString}" );
            }, null );
        }
        #endregion

        private static T LoadOverridesFromAllFiles( T runtimeInstance = null )
        {
            var mainJsonFilename = "Settings.json";
            runtimeInstance = LoadOverridesFromFile( runtimeInstance, mainJsonFilename, jsonPath: filename );

            var jsonFilename = filename + ".json";
            runtimeInstance = LoadOverridesFromFile( runtimeInstance, jsonFilename );

            return runtimeInstance;
        }

        private static T LoadOverridesFromFile( T runtimeInstance, string jsonFilePath, string jsonPath = null )
        {
            T localRuntimeInstance = null;
            try
            {
                if( File.Exists( jsonFilePath ) )
                {
                    var json = File.ReadAllText( jsonFilePath );
                    if( jsonPath != null )
                    {
                        var jToken = JObject.Parse( json ).SelectToken( jsonPath );
                        if( jToken == null )
                            return runtimeInstance;

                        json = jToken.ToString();
                    }

                    if( runtimeInstance == null )
                        localRuntimeInstance = runtimeInstance = ScriptableObject.Instantiate( _instance );

                    JsonConvert.PopulateObject( json, runtimeInstance );
                    AddOverrideOriginFilePath( runtimeInstance, jsonFilePath );

                    return runtimeInstance;
                }
            }
            catch( Exception ex )
            {
                Debug.LogError( $"Error loading overrides from {jsonFilePath} for {typeof( T ).Name}\n{ex}" );

                if( localRuntimeInstance )
                {
#if UNITY_EDITOR
                    if( Application.isPlaying == false )
                        ScriptableObject.DestroyImmediate( localRuntimeInstance );
                    else
#endif
                        ScriptableObject.Destroy( localRuntimeInstance );
                }
            }

            return runtimeInstance;
        }

        private static void AddOverrideOriginFilePath( T instance, string filePath )
        {
            var oi = ( ( IOverridableSettings )instance );
            if( oi == null )
                return;

            if( oi.overrideOriginFilePaths == null )
                oi.overrideOriginFilePaths = new List<string>();
            else
            {
                if( oi.overrideOriginFilePaths.Contains( filePath ) )
                    return;
            }

            oi.overrideOriginFilePaths.Add( filePath );
        }

#if UNITY_EDITOR
        // Check if we are leaving play mode and reinitialize to revert from runtime instance.
        private static void EditorApplication_playModeStateChanged( PlayModeStateChange stateChange )
        {
            if( stateChange != PlayModeStateChange.ExitingPlayMode )
                return;

            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;

            ClearOriginFileWatchers();

            _instance = null;
            Initialize();
        }
#endif

        public void SaveAsJsonFile( string filename = null )
        {
            filename = GetFilenameWithExtension( filename, ".json" );

            using( var fs = File.CreateText( filename ) )
            {
                fs.Write( JsonConvert.SerializeObject( this, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { ContractResolver = UnityEngineObjectContractResolver.instance } ) );
            }
        }

        public void LoadFromJsonFile( string filename = null )
        {
            filename = GetFilenameWithExtension( filename, ".json" );

            if( File.Exists( filename ) == false )
                return;

            var json = File.ReadAllText( filename );
            JsonConvert.PopulateObject( json, this );
        }

        private static string GetFilenameWithExtension( string filename, string extension )
        {
            if( filename == null )
                filename = attribute.filename ?? typeof( T ).Name;

            if( filename.EndsWith( extension ) == false )
                filename += extension;

            return filename;
        }

        private class UnityEngineObjectContractResolver : DefaultContractResolver
        {
            public static UnityEngineObjectContractResolver instance { get; } = new UnityEngineObjectContractResolver();

            private UnityEngineObjectContractResolver() { }

            private static string[] _ignoredMemberNames = new[] { nameof( UnityEngine.Object.name ), nameof( UnityEngine.Object.hideFlags ) };

            protected override JsonProperty CreateProperty( MemberInfo member, MemberSerialization memberSerialization )
            {
                var property = base.CreateProperty( member, memberSerialization );

                if( property.Ignored )
                    return property;

                if( member.DeclaringType == typeof( UnityEngine.Object ) && _ignoredMemberNames.Contains( member.Name ) )
                    property.Ignored = true;

                return property;
            }
        }
    }
}
