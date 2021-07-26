// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System.Xml;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Linq;

namespace Hextant
{
    // Base class for project/users settings. Use the [Settings] attribute to
    // specify its usage, display path, and filename.
    // * Settings are stored in Assets/Settings/ folder.
    // * Important! The user settings folder Assets/Settings/Editor/User/ must be
    //   excluded from source control.
    // * User settings will be placed in a subdirectory named the same as
    //   the current project folder so that shallow cloning (symbolic links to
    //   the Assets/ folder) can be used when testing multiplayer games.
    // See: https://HextantStudios.com/unity-custom-settings/
    public abstract class Settings<T> : ScriptableObject where T : Settings<T>
    {
        // The singleton instance. (Not thread safe but fine for ScriptableObjects.)
        public static T instance => _instance != null ? _instance : Initialize();
        static T _instance;

        // Loads or creates the settings instance and stores it in _instance.
        protected static T Initialize()
        {
            // If the instance is already valid, return it. Needed if called from a 
            // derived class that wishes to ensure the settings are initialized.
            if( _instance != null ) return _instance;

            // Verify there was a [Settings] attribute.
            if( attribute == null )
            {
                Debug.LogError( "[Settings] attribute missing for type: " +
                    typeof( T ).Name );
                return null;
            }

            // Attempt to load the settings asset.
            var filename = attribute.filename ?? typeof( T ).Name;
            var path = GetSettingsPath() + filename + ".asset";

            if( attribute is IRuntimeSettingsAttribute )
                _instance = Resources.Load<T>( filename );
#if UNITY_EDITOR
            else
                _instance = AssetDatabase.LoadAssetAtPath<T>( path );

            if( _instance == null )
                TryLocateAndMoveAsset( path );
#endif

            // Create the settings instance if it was not loaded or found.
            if( _instance == null )
                CreateAsset( path );

            // Load runtime overrides from json file if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && (attribute as IRuntimeSettingsAttribute ).allowRuntimeFileOverrides
#if UNITY_EDITOR
              && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                _instance.TryLoadRuntimeFileOverrides( filename );

            return _instance;
        }

#if UNITY_EDITOR
        private static void TryLocateAndMoveAsset( string path )
        {
            // Move settings if its path changed (type renamed or attribute changed)
            // while the editor was running. This must be done manually if the
            // change was made outside the editor.
            var instances = Resources.FindObjectsOfTypeAll<T>();
            if( instances.Length > 0 )
            {
                var oldPath = AssetDatabase.GetAssetPath( instances[ 0 ] );
                var result = AssetDatabase.MoveAsset( oldPath, path );
                if( string.IsNullOrEmpty( result ) )
                    _instance = instances[ 0 ];
                else
                    Debug.LogWarning( $"Failed to move previous settings asset " +
                        $"'{oldPath}' to '{path}'. " +
                        $"A new settings asset will be created.", _instance );
            }
        }
#endif

        private static void CreateAsset( string path )
        {
            _instance = CreateInstance<T>();

#if UNITY_EDITOR
            var script = MonoScript.FromScriptableObject( _instance );
            if( script == null || ( script.name != _instance.GetType().Name ) )
            {
                Debug.LogError( $"Cannot create ScriptableObject. Script filename has to match the class name: {_instance.GetType().Name}", _instance );
                _instance = null;
                return;
            }

            // Create a new settings instance if it was not found.
            // Create the directory as Unity does not do this itself.
            Directory.CreateDirectory( Path.Combine(
                Directory.GetCurrentDirectory(),
                Path.GetDirectoryName( path ) ) );

            // Create the asset only in the editor.
            AssetDatabase.CreateAsset( _instance, path );
#endif
        }

        private static void TryLoadRuntimeFileOverrides( string filename )
        {
            var xmlFilename = filename + ".xml";
            if( File.Exists( xmlFilename ) )
            {
                var xml = File.ReadAllText( xmlFilename );
                var doc = new XmlDocument();
                doc.LoadXml( xml );

                var json = JsonConvert.SerializeXmlNode( doc, Newtonsoft.Json.Formatting.None, omitRootObject: true );

                CreateRuntimeInstanceWithOverrides( json );
            }

            var jsonFilename = filename + ".json";
            if( File.Exists( jsonFilename ) )
            {
                var json = File.ReadAllText( jsonFilename );

                CreateRuntimeInstanceWithOverrides( json );
            }
        }

        private static void CreateRuntimeInstanceWithOverrides( string json )
        {
            var runtimeInstanceName = _instance.name + " (Runtime instance with overrides from file)";
            var runtimeInstance = ScriptableObject.Instantiate( _instance );
            runtimeInstance.name = runtimeInstanceName;

            JsonConvert.PopulateObject( json, runtimeInstance );

            _instance = runtimeInstance;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif
        }

        private static void SaveToJsonFile( string filename )
        {
            var jsonFilename = filename + ".json";

            using( var fs = File.CreateText( jsonFilename ) )
            {
                fs.Write( JsonConvert.SerializeObject( _instance, Newtonsoft.Json.Formatting.Indented, new JsonSerializerSettings { ContractResolver = UnityEngineObjectContractResolver.instance } ) );
            }
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

        private static void SaveToXmlFile( string filename )
        {
            var xmlFilename = filename + ".xml";

            var overrides = new XmlAttributeOverrides();
            var ignoreAttributes = new XmlAttributes { XmlIgnore = true };
            overrides.Add( typeof( UnityEngine.Object ), nameof( name ), ignoreAttributes );
            overrides.Add( typeof( UnityEngine.Object ), nameof( hideFlags ), ignoreAttributes );

            using( var fs = new FileStream( xmlFilename, FileMode.Create ) )
            {
                var xs = new XmlSerializer( typeof( T ), overrides );
                xs.Serialize( fs, _instance );
            }
        }

        // Returns the full asset path to the settings file.
        static string GetSettingsPath()
        {
            var path = "Assets/Settings/";

            switch( attribute.usage )
            {
                case SettingsUsage.RuntimeProject:
                    path += "Resources/"; break;
#if UNITY_EDITOR
                case SettingsUsage.EditorProject:
                    path += "Editor/"; break;
                case SettingsUsage.EditorUser:
                    path += "Editor/User/" + GetProjectFolderName() + '/'; break;
#endif
                default: throw new System.InvalidOperationException();
            }
            return path;
        }

        // The derived type's [Settings] attribute.
        public static SettingsAttributeBase attribute =>
            _attribute != null ? _attribute : _attribute =
                typeof( T ).GetCustomAttribute<SettingsAttributeBase>( true );
        static SettingsAttributeBase _attribute;

        internal static string displayPath { get; } = ( attribute.usage == SettingsUsage.EditorUser ? "Preferences/" : "Project/" ) +
            ( attribute.displayPath != null ? attribute.displayPath : typeof( T ).Name );

        // Called to validate settings changes.
        protected virtual void OnValidate() { }

        // Sets the specified setting to the desired value and marks the settings
        // so that it will be saved.
        protected void Set<S>( ref S setting, S value )
        {
            if( EqualityComparer<S>.Default.Equals( setting, value ) ) return;
            setting = value;
#if UNITY_EDITOR
            OnValidate();
            SetDirty();
#endif
        }

#if UNITY_EDITOR
        // Marks the settings dirty so that it will be saved.
        protected new void SetDirty() => EditorUtility.SetDirty( this );

        // The directory name of the current project folder.
        static string GetProjectFolderName()
        {
            var path = Application.dataPath.Split( '/' );
            return path[ path.Length - 2 ];
        }

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

        // Base class for settings contained by a Settings<T> instance.
        [Serializable]
        public abstract class SubSettings
        {
            // Called when a setting is modified.
            protected virtual void OnValidate() { }

            // Sets the specified setting to the desired value and marks the settings
            // instance so that it will be saved.
            protected void Set<S>( ref S setting, S value )
            {
                if( EqualityComparer<S>.Default.Equals( setting, value ) ) return;
                setting = value;
#if UNITY_EDITOR
                OnValidate();
                instance.SetDirty();
#endif
            }
        }
    }
}