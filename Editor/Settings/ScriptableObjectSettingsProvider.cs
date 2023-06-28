// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;
using System.IO;
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
        private ScriptableObject _settingsScriptableObject;
        private ISettingsInternals _settingsInternals;
        private ISerializableSettings _serializableSettings;
        private IOverridableSettings _overridableSettings;
        private bool _isRuntimeInstance;

        // True if the keywords set has been built.
        bool _keywordsBuilt;

        // Cached editor used to render inspector GUI.
        Editor _editor;

#if ODIN_INSPECTOR
        private static bool _doneWaitingForOdin;
        private bool _waitingForOdin;
#endif

        // Called when the settings are displayed in the UI.
        public override void OnActivate( string searchContext,
            UnityEngine.UIElements.VisualElement rootElement )
        {
            _settingsScriptableObject = _settingsGetter();
            _settingsInternals = _settingsScriptableObject as ISettingsInternals;
            _serializableSettings = _settingsScriptableObject as ISerializableSettings;
            _overridableSettings = _settingsScriptableObject as IOverridableSettings;
            _isRuntimeInstance = string.IsNullOrEmpty( AssetDatabase.GetAssetPath( _settingsScriptableObject ) );

#if ODIN_INSPECTOR
            if( _doneWaitingForOdin == false)
                _waitingForOdin = true;
            else
#endif
            CreateEditor();

            base.OnActivate( searchContext, rootElement );
        }

        // Called when the settings are no longer displayed in the UI.
        public override void OnDeactivate()
        {
            if( _editor != null )
            {
                Editor.DestroyImmediate( _editor );
                _editor = null;
            }

            base.OnDeactivate();
        }

        public override void OnTitleBarGUI()
        {
            base.OnTitleBarGUI();

            if( _serializableSettings == null )
                return;

            var dropdownRect = EditorGUILayout.GetControlRect();
            var dropdownButtonRect = dropdownRect;
            dropdownButtonRect.x += dropdownRect.width - dropdownRect.height * 2;
            dropdownButtonRect.width = dropdownRect.height * 2;
            if( EditorGUI.DropdownButton( dropdownButtonRect, EditorGUIUtility.IconContent( "SaveAs" ), FocusType.Keyboard ) )
            {
                var menu = new GenericMenu();
                /*
                menu.AddItem( new GUIContent( "Load all overrides" ), false, () =>
                {
                    Undo.RecordObject( _settingsScriptableObject, "Load all overrides" );
                    //_serializableSettings.LoadAllOverrides();
                    Undo.FlushUndoRecordObjects();
                } );
                */
                menu.AddItem( new GUIContent( "Load from .json" ), false, () =>
                {
                    var directory = Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
                    var filename = EditorUtility.OpenFilePanel( $"Load from .json", directory, "json" );
                    if( string.IsNullOrEmpty( filename ) )
                        return;

                    Undo.RecordObject( _settingsScriptableObject, "Load from .json" );
                    _serializableSettings.LoadFromJsonFile( filename );
                    Undo.FlushUndoRecordObjects();
                } );
                menu.AddItem( new GUIContent( "Save as .json" ), false, () =>
                {
                    var directory = Path.GetFullPath( Path.Combine( Application.dataPath, ".." ) );
                    var filename = EditorUtility.SaveFilePanel( $"Save as .json", directory, _settingsInternals.Filename, "json" );
                    if( string.IsNullOrEmpty( filename ) )
                        return;

                    _serializableSettings.SaveAsJsonFile( filename );
                } );
                menu.DropDown( dropdownButtonRect );
            }
        }

        // Displays the settings.
        public override void OnGUI( string searchContext )
        {
#if ODIN_INSPECTOR
            // Delay editor creation one frame so to be sure Odin is initialized
            if(_doneWaitingForOdin == false)
            {
                if( Event.current.type != EventType.Repaint )
                    return;

                if( _waitingForOdin )
                {
                    _waitingForOdin = false;
                }
                else
                {
                    CreateEditor();
                    _doneWaitingForOdin = true;
                }

                Repaint();
                return;
            }
#endif
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

                if( _overridableSettings.overrideOriginFilePaths != null )
                {
                    var file_s = $"file{( _overridableSettings.overrideOriginFilePaths.Count > 1 ? "s" : "" )}";
                    var re_loaded = _overridableSettings.useOriginFileWatchers ? "are being auto-reloaded" : "have been loaded";
                    GUI.Label( EditorGUILayout.GetControlRect(), $"Overrides {re_loaded} from {file_s}:" );
                    EditorGUI.indentLevel++;
                    foreach(var o in _overridableSettings.overrideOriginFilePaths)
                    {
                        GUI.Label( EditorGUILayout.GetControlRect(), o );
                    }
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space( 10 );
            }

            // Draw the editor's GUI.
            _editor.OnInspectorGUI();

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

        private void CreateEditor()
        {
            _editor = Editor.CreateEditor( _settingsScriptableObject );
        }
    }
}
