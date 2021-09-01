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
    /*
    internal class MissingSettingsAttributeException : Exception
    {
        private string _typeName;

        public MissingSettingsAttributeException( string typeName )
        {
            _typeName = typeName;
        }

        public override string Message => $"{_typeName} inherits from Settings<> but ";
    }
    */

    internal interface ISettings
    {
        internal string filename { get; }
    }

    // Base class for project/users settings. Use the [Settings] attribute to
    // specify its usage, display path, and filename.
    // * Settings are stored in Assets/Settings/ folder.
    // * Important! The user settings folder Assets/Settings/Editor/User/ must be
    //   excluded from source control.
    // * User settings will be placed in a subdirectory named the same as
    //   the current project folder so that shallow cloning (symbolic links to
    //   the Assets/ folder) can be used when testing multiplayer games.
    // See: https://HextantStudios.com/unity-custom-settings/
    public abstract class Settings<T> : ScriptableObject, ISettings where T : Settings<T>
    {
        // The singleton instance. (Not thread safe but fine for ScriptableObjects.)
        public static T instance => _instance != null ? _instance : Initialize();
        protected static T _instance;

        // The derived type's [Settings] attribute.
        internal static SettingsAttributeBase attribute { get; } = typeof( T ).GetCustomAttribute<SettingsAttributeBase>( true );
        internal static string filename => attribute?.filename ?? typeof( T ).Name;
        internal static string displayPath => ( attribute.usage == SettingsUsage.EditorUser ? "Preferences/" : "Project/" ) +
            ( attribute.displayPath != null ? attribute.displayPath : typeof( T ).Name );

        string ISettings.filename => Settings<T>.filename;

        // Loads or creates the settings instance and stores it in _instance.
        protected static T Initialize()
        {
            // If the instance is already valid, return it. Needed if called from a 
            // derived class that wishes to ensure the settings are initialized.
            if( _instance != null ) return _instance;

            // Verify there was a [Settings] attribute.
            if( attribute == null )
            {
                var availableAttributes = string.Join( ",", new[] { nameof( EditorUserSettingsAttribute ), nameof( EditorProjectSettingsAttribute ), nameof( RuntimeProjectSettingsAttribute ) } );
                Debug.LogError( $"SettingsAttribute missing for type: { typeof( T ).Name }. Please use either: {availableAttributes}" );
                return null;
            }

            // Attempt to load the settings asset.
            var path = GetSettingsPath() + filename + ".asset";

            _instance = LoadAsset( filename, path );

#if UNITY_EDITOR
            if( _instance == null )
                _instance = TryLocateAndMoveAsset( filename, path );
#endif

            // Create the settings instance if it was not loaded or found.
            if( _instance == null )
                CreateAsset( path );

            _instance.InitializeInstance();

            return _instance;
        }

        private static T LoadAsset( string filename, string path )
        {
            if( attribute is IRuntimeSettingsAttribute )
                return Resources.Load<T>( filename );
            else
#if UNITY_EDITOR
                return AssetDatabase.LoadAssetAtPath<T>( path );
#else
               return null;
#endif
        }

        protected virtual void InitializeInstance()
        { }

#if UNITY_EDITOR
        private static T TryLocateAndMoveAsset( string filename, string path )
        {
            // Move settings if its path changed (type renamed or attribute changed)
            // while the editor was running. This must be done manually if the
            // change was made outside the editor.
            var instances = Resources.FindObjectsOfTypeAll<T>()
                .Where( i => string.IsNullOrEmpty( AssetDatabase.GetAssetPath( i ) ) == false )
                .ToList();

            if( instances.Count > 0 )
            {
                var oldPath = AssetDatabase.GetAssetPath( instances[ 0 ] );

                // check if inside Assets folder: move the asset to the default path, otherwise copy it there
                if( oldPath.StartsWith( "Assets/" ) )
                {
                    var result = AssetDatabase.MoveAsset( oldPath, path );
                    if( string.IsNullOrEmpty( result ) )
                        return instances[ 0 ];
                    else
                        Debug.LogWarning( $"Failed to move previous settings asset " +
                            $"'{oldPath}' to '{path}'. " +
                            $"A new settings asset will be created.", _instance );
                }
                else
                {
                    try
                    {
                        var clone = ScriptableObject.Instantiate( instances[ 0 ] );
                        AssetDatabase.CreateAsset( clone, path );
                        return clone;
                    }
                    catch( Exception ex )
                    {
                        Debug.LogWarning( $"Failed to copy previous settings asset " +
                            $"'{oldPath}' to '{path}'. " +
                            $"A new settings asset will be created." +
                            $"{ex}", _instance );
                    }
                }
            }

            return null;
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
            return Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
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