using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Hextant
{
    public interface ISerializableSettings
    {
        string SaveAsJsonFile( string filename = null );
        void LoadFromJsonFile( string filename = null );
    }

    public abstract class SerializableSettings<T> : Settings<T>, ISerializableSettings where T : SerializableSettings<T>
    {
        protected override void InitializeInstance()
        {
            var filename = attribute.filename ?? typeof( T ).Name;

            // Load runtime overrides from json file if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && ( attribute as IRuntimeSettingsAttribute ).allowRuntimeFileOverrides
#if UNITY_EDITOR
                          && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                _instance = TryLoadRuntimeFileOverrides( _instance, filename );
        }

        internal static T TryLoadRuntimeFileOverrides( T instance, string filename )
        {
            var jsonFilename = filename + ".json";
            if( File.Exists( jsonFilename ) )
            {
                var json = File.ReadAllText( jsonFilename );

                instance = CreateRuntimeInstanceWithOverridesFromJson( instance, json );
            }

            return instance;
        }

        private static T CreateRuntimeInstanceWithOverridesFromJson( T instance, string json )
        {
            var runtimeInstanceName = instance.name + " (Runtime instance with overrides)";
            var runtimeInstance = ScriptableObject.Instantiate( instance );
            runtimeInstance.name = runtimeInstanceName;

            JsonConvert.PopulateObject( json, runtimeInstance );

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif

            return runtimeInstance;
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

        public string SaveAsJsonFile( string filename = null )
        {
            filename = GetFilenameWithExtension( filename, ".json" );

            using( var fs = File.CreateText( filename ) )
            {
                fs.Write( JsonConvert.SerializeObject( this, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { ContractResolver = UnityEngineObjectContractResolver.instance } ) );
            }

            return filename;
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
