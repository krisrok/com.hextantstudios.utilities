﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        private static JsonSerializer _jsonSerializer;
        private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = UnityEngineObjectContractResolver.Instance,
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented
        };

        List<string> IOverridableSettings.overrideOriginFilePaths { get; set; }
        bool IOverridableSettings.useOriginFileWatchers { get; set; }

        protected override void InitializeInstance()
        {
            // Load runtime overrides from json file if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && ( attribute as IRuntimeSettingsAttribute ).AllowsFileOverrides()
#if UNITY_EDITOR
                          && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                _instance = LoadRuntimeFileOverrides();

            // Load runtime overrides from commandline if it's allowed and we're actually in runtime.
            if( attribute is IRuntimeSettingsAttribute && ( attribute as IRuntimeSettingsAttribute ).AllowsCommandlineOverrides()
#if UNITY_EDITOR
                          && EditorApplication.isPlayingOrWillChangePlaymode
#endif
            )
                _instance = LoadRuntimeCommandlineOverrides();
        }

        internal static T LoadRuntimeCommandlineOverrides()
        {
            //var args = Environment.GetCommandLineArgs();
            var args = new[] { "unity.exe", "-test1", "-settings:TestSettings.Sub.Integer=55", "-s:TestSettings.Boolean=false" };
            var runtimeInstance = _instance;

            foreach( var settingsArg in args.Where( arg => arg.StartsWith( "-settings:" ) || arg.StartsWith( "-s:" ) ) )
            {
                var colonIndex = settingsArg.IndexOf( ':' ) + 1;
                if( settingsArg.Length - colonIndex <= 0 )
                {
                    Debug.LogWarning( $"Invalid settings argument format ({settingsArg}): Missing assignment" );
                    continue;
                }

                var assignment = settingsArg.Substring( colonIndex );

                var assignmentParts = assignment.Split( new[] { '=' }, StringSplitOptions.RemoveEmptyEntries );
                if( assignmentParts.Length < 2 )
                {
                    Debug.LogWarning( $"Invalid settings argument format ({settingsArg}): Missing '='" );
                    continue;
                }

                var propertyPath = assignmentParts[ 0 ];
                var value = assignmentParts[ 1 ];

                var propertyPathParts = propertyPath.Split( new[] { '.' }, StringSplitOptions.RemoveEmptyEntries );
                if( propertyPathParts.Length < 2 )
                {
                    Debug.LogWarning( $"Invalid settings argument format ({settingsArg}): Property path too short" );
                    continue;
                }

                var propertyPathSettingsName = propertyPathParts[ 0 ];
                if( propertyPathSettingsName == filename || propertyPathSettingsName == typeof( T ).Name )
                {
                    var root = new JObject();
                    var current = root;
                    for( var i = 1; i < propertyPathParts.Length; i++ )
                    {
                        if( propertyPathParts.Length - 1 == i )
                        {
                            current.Add( propertyPathParts[ i ], JValue.CreateString( value ) );
                        }
                        else
                        {
                            current.Add( propertyPathParts[ i ], current = new JObject() );
                        }
                    }

                    if( runtimeInstance == null )
                        runtimeInstance = ScriptableObject.Instantiate( _instance );

                    using( var jsonReader = root.CreateReader() )
                        _jsonSerializer.Populate( jsonReader, _instance );

                    AddOverrideOriginFilePath( runtimeInstance, settingsArg );
                }
            }

            return runtimeInstance;
        }

        internal static T LoadRuntimeFileOverrides()
        {
            var runtimeInstance = LoadOverridesFromAllFiles();

            if( runtimeInstance == null )
                return null;

#if UNITY_EDITOR
            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
#endif

            var overridableSettings = ( IOverridableSettings )runtimeInstance;
            var overridesString = $"with overrides from file{( overridableSettings.overrideOriginFilePaths.Count > 1 ? "s" : "" )}: {string.Join( ", ", overridableSettings.overrideOriginFilePaths )}";
            Debug.Log( $"Created {typeof( T ).Name} runtime instance {overridesString}" );

            runtimeInstance.name = $"{instance.name} ({overridesString})";

            if( ( attribute as IRuntimeSettingsAttribute ).AllowsFileWatchers() )
                SetupOriginFileWatchers( overridableSettings );

            return runtimeInstance;
        }

        #region filewatchers
        private static void SetupOriginFileWatchers( IOverridableSettings overridableSettings )
        {
            overridableSettings.useOriginFileWatchers = true;

            Application.onBeforeRender += Application_onBeforeRender;

            foreach( var originFilePath in overridableSettings.overrideOriginFilePaths )
            {
                var fsw = new FileSystemWatcher( Path.GetDirectoryName( Path.GetFullPath( originFilePath ) ), Path.GetFileName( originFilePath ) );
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
                if( Path.IsPathRooted( jsonFilePath ) == false )
                    jsonFilePath = Path.GetFullPath( Path.Combine( Application.dataPath, "..", jsonFilePath ) );

                if( File.Exists( jsonFilePath ) )
                {
                    using var jr = new JsonTextReader( File.OpenText( jsonFilePath ) )
                    {
                        DateParseHandling = DateParseHandling.None
                    };

                    JToken jToken = JObject.Load( jr );

                    if( jToken == null )
                        return runtimeInstance;

                    if( jsonPath != null )
                        jToken = jToken.SelectToken( jsonPath );

                    if( jToken == null )
                        return runtimeInstance;

                    if( runtimeInstance == null )
                        localRuntimeInstance = runtimeInstance = ScriptableObject.Instantiate( _instance );

                    if( _jsonSerializer == null )
                        _jsonSerializer = JsonSerializer.Create( _jsonSerializerSettings );

                    using( var jsonReader = jToken.CreateReader() )
                        _jsonSerializer.Populate( jsonReader, runtimeInstance );

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
            if( filename == null )
                filename = SerializableSettings<T>.filename;

            filename = GetFilenameWithExtension( filename, ".json" );

            using( var fs = File.CreateText( filename ) )
            {
                fs.Write( JsonConvert.SerializeObject( this, _jsonSerializerSettings ) );
            }
        }

        public void LoadFromJsonFile( string filename = null )
        {
            if( filename == null )
                filename = SerializableSettings<T>.filename;

            filename = GetFilenameWithExtension( filename, ".json" );

            if( File.Exists( filename ) == false )
                return;

            var json = File.ReadAllText( filename );
            JsonConvert.PopulateObject( json, this, _jsonSerializerSettings );
        }

        private static string GetFilenameWithExtension( string filename, string extension )
        {
            if( filename.EndsWith( extension ) == false )
                filename += extension;

            return filename;
        }

        }
    }
}
