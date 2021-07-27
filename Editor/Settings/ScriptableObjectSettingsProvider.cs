// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;
using UnityEditor;
using UnityEngine;

namespace Hextant.Editor
{
    using Editor = UnityEditor.Editor;

    // SettingsProvider helper used to display settings for a ScriptableObject
    // derived class.
    public class ScriptableObjectSettingsProvider : SettingsProvider
    {
        public ScriptableObjectSettingsProvider( Func<ScriptableObject> settingsGetter,
            SettingsScope scope, string displayPath ) :
            base( displayPath, scope ) => _settingsGetter = settingsGetter;

        private readonly Func<ScriptableObject> _settingsGetter;

        // The settings instance being edited.
        private ScriptableObject _settings;
        private ISerializableSettings _serializableSettings;
        private bool _isRuntimeInstance;

        // Called when the settings are displayed in the UI.
        public override void OnActivate( string searchContext,
            UnityEngine.UIElements.VisualElement rootElement )
        {
            _settings = _settingsGetter();
            _serializableSettings = _settings as ISerializableSettings;
            _isRuntimeInstance = string.IsNullOrEmpty( AssetDatabase.GetAssetPath( _settings ) );
            _editor = Editor.CreateEditor( _settings );
            base.OnActivate( searchContext, rootElement );
        }

        // Called when the settings are no longer displayed in the UI.
        public override void OnDeactivate()
        {
            Editor.DestroyImmediate( _editor );
            _editor = null;
            base.OnDeactivate();
        }

        // Displays the settings.
        public override void OnGUI( string searchContext )
        {
            if( _settingsGetter == null || _editor == null ) return;

            // Set label width and indentation to match other settings.
            EditorGUIUtility.labelWidth = 250;
            GUILayout.BeginHorizontal();
            GUILayout.Space( 10 );
            GUILayout.BeginVertical();
            GUILayout.Space( 10 );

            if( _isRuntimeInstance )
            {
                GUI.Label( EditorGUILayout.GetControlRect(), "This is a runtime instance: Changes will NOT be saved automatically!", EditorStyles.boldLabel );
                GUI.Label( EditorGUILayout.GetControlRect(), "Overrides may have been loaded from file." );
                GUILayout.Space( 10 );
            }

            // Draw the editor's GUI.
            _editor.OnInspectorGUI();

            if( _serializableSettings != null )
            {
                GUILayout.Space( 10 );
                EditorGUILayout.BeginHorizontal();

                if( GUILayout.Button( $"Save as .json" ) )
                {
                    _serializableSettings.SaveAsJsonFile();
                }

                if( GUILayout.Button( $"Load from .json" ) )
                {
                    Undo.RecordObject( _settings, "Load from .json" );
                    _serializableSettings.LoadFromJsonFile();
                    Undo.FlushUndoRecordObjects();
                }

                EditorGUILayout.EndHorizontal();
            }

            // Reset label width and indent.
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            EditorGUIUtility.labelWidth = 0;
        }

        // Build the set of keywords on demand from the settings fields.
        public override bool HasSearchInterest( string searchContext )
        {
            if( !_keywordsBuilt )
            {
                using( var serializedSettings = new SerializedObject( _settingsGetter() ) )
                {
                    keywords = GetSearchKeywordsFromSerializedObject( serializedSettings );
                }
                _keywordsBuilt = true;
            }
            return base.HasSearchInterest( searchContext );
        }

        // True if the keywords set has been built.
        bool _keywordsBuilt;

        // Cached editor used to render inspector GUI.
        Editor _editor;
    }
}
