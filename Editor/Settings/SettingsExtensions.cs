// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace Hextant.Editor
{
    public static class SettingsExtensions
    {
        // The SettingsProvider instance used to display settings in Edit/Preferences
        // and Edit/Project Settings.
        public static SettingsProvider GetSettingsProvider<T>() where T : Settings<T>
        {
            Debug.Assert( Settings<T>.attribute.displayPath != null );
            var instanceProp = typeof( Settings<T> ).GetProperty( nameof( Settings<T>.instance ), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
            return new ScriptableObjectSettingsProvider( () => (ScriptableObject)instanceProp.GetValue(null),
                Settings<T>.attribute.usage == SettingsUsage.EditorUser ?
                SettingsScope.User : SettingsScope.Project,
                Settings<T>.attribute.displayPath );
        }
    }
}
