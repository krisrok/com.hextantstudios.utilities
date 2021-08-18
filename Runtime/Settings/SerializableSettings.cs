using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        internal string[] overrideOrigins { get; set; }
    }

    public abstract class SerializableSettings<T> : Settings<T>, ISerializableSettings, IOverridableSettings where T : SerializableSettings<T>
    {
        string[] IOverridableSettings.overrideOrigins { get; set; }

        protected override void InitializeInstance()
        {
            var filename = attribute.filename ?? typeof( T ).Name;

            // Load runtime overrides from json file if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && ( attribute as IRuntimeSettingsAttribute ).allowRuntimeFileOverrides
#if UNITY_EDITOR
                          && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                _instance = LoadRuntimeFileOverrides( _instance, filename );
        }

        internal static T LoadRuntimeFileOverrides( T instance, string filename )
        {
            T runtimeInstance = null;

            var mainJsonFilename = "Settings.json";
            runtimeInstance = TryLoadOverrides( runtimeInstance, mainJsonFilename, jsonPath: filename );

            var jsonFilename = filename + ".json";
            runtimeInstance = TryLoadOverrides( runtimeInstance, jsonFilename );

            // return original if there are no successful overrides
            if( runtimeInstance == null )
                return instance;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif

            var overridableSettings = ( IOverridableSettings )runtimeInstance;
            var overridesString = $"with overrides from file{( overridableSettings.overrideOrigins.Length > 1 ? "s" : "" )}: {string.Join( ", ", overridableSettings.overrideOrigins )}";
            Debug.Log( $"Created {typeof( T ).Name} runtime instance {overridesString}" );

            runtimeInstance.name = $"{instance.name} ({overridesString})";

            return runtimeInstance;
        }

        private static T TryLoadOverrides( T runtimeInstance, string jsonFilename, string jsonPath = null )
        {
            T localRuntimeInstance = null;
            try
            {
            if( File.Exists( jsonFilename ) )
            {
                var json = File.ReadAllText( jsonFilename );
                    if( jsonPath != null )
                    {
                        var jToken = JObject.Parse( json ).SelectToken( jsonPath );
                        if( jToken == null )
                            return runtimeInstance;

                        json = jToken.ToString();
            }

                    if( runtimeInstance == null )
                        localRuntimeInstance = runtimeInstance = ScriptableObject.Instantiate( _instance );

                    ApplyOverrides( runtimeInstance, json, jsonFilename );
                    return runtimeInstance;
                }
            }
            catch( Exception ex )
            {
                Debug.LogError( $"Error loading overrides from {jsonFilename} for {typeof( T ).Name}\n{ex}" );

                if( localRuntimeInstance )
                    ScriptableObject.Destroy( localRuntimeInstance );
        }

            return runtimeInstance;
        }

        private static void ApplyOverrides( T instance, string json, string origin )
        {
            JsonConvert.PopulateObject( json, instance );

            AddOverrideOrigin( instance, origin );
        }

        private static void AddOverrideOrigin( T instance, string origin )
        {
            var oi = ( ( IOverridableSettings )instance );
            if( oi == null )
                return;

            List<string> origins;
            if( oi.overrideOrigins == null )
                origins = new List<string>();
            else
                origins = new List<string>( oi.overrideOrigins );

            origins.Add( origin );

            oi.overrideOrigins = origins.ToArray();
        }

#if UNITY_EDITOR
        // Check if we are leaving play mode and reinitialize to revert from runtime instance.
        private static void EditorApplication_playModeStateChanged( PlayModeStateChange stateChange )
        {
            if( stateChange != PlayModeStateChange.ExitingPlayMode )
                return;

            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;

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

        public void LoadFromJsonFile( string filename = null)
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
