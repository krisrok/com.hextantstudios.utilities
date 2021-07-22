using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hextant.Editor
{
    [CustomPropertyDrawer( typeof( Settings<> ), true )]
    public class ScriptableObjectSettingsDrawer : PropertyDrawer
    {
        private static Dictionary<Type, SettingsAttribute> _settingsAttributeLookup = new Dictionary<Type, SettingsAttribute>();

        public override void OnGUI( Rect position, SerializedProperty property, GUIContent label )
        {
            var settingsInstance = ( ( ScriptableObject )property.objectReferenceValue );

            var attribute = GetSettingsAttribute( fieldInfo.FieldType );

            var textFieldRect = position;
            textFieldRect.width -= 20;
            if( textFieldRect.Contains( Event.current.mousePosition ) && Event.current.type == EventType.MouseDown && Event.current.button == 0 )
            {
                // Jump to project settings
                if( settingsInstance == null )
                {
                    if( attribute != null )
                        SettingsService.OpenProjectSettings( _settingsAttributeLookup[ fieldInfo.FieldType ].displayPath );
                }
                // Or ping the object in project window
                else
                {
                    EditorGUIUtility.PingObject( property.objectReferenceValue );
                }
            }

            EditorGUI.PropertyField( position, property, label );

            if( settingsInstance == null && attribute != null)
            {
                var name = attribute?.displayPath;

                // Clear ObjectField drawer
                textFieldRect.xMin += EditorGUIUtility.labelWidth;
                EditorGUI.LabelField( textFieldRect, GUIContent.none, EditorStyles.textField );

                // Leave some space for icon and draw name
                textFieldRect.xMin += textFieldRect.height;
                EditorGUI.LabelField( textFieldRect, name, EditorStyles.textField );

                // Draw cogwheel icon
                var iconRect = position;
                iconRect.x += EditorGUIUtility.labelWidth;
                iconRect.width = iconRect.height;
                EditorGUI.LabelField( iconRect, EditorGUIUtility.IconContent( "Settings" ) );
            }

            label.tooltip = null;
        }

        private static SettingsAttribute GetSettingsAttribute( Type type )
        {
            if( _settingsAttributeLookup.ContainsKey( type ) == false )
            {
                return _settingsAttributeLookup[ type ] = type.GetCustomAttributes( typeof( SettingsAttribute ), true ).FirstOrDefault() as SettingsAttribute;
            }

            return _settingsAttributeLookup[ type ];
        }
    }
}
