// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using UnityEngine;
using UnityEditor;
using System.Reflection;
using System;

namespace Hextant.Editor
{
    public static class SettingsExtensions
    {
        // The SettingsProvider instance used to display settings in Edit/Preferences
        // and Edit/Project Settings.
        [Obsolete( "Use overload with type inference instead: SettingsExtensions.GetSettingsProvider(() => instance)" )]
        public static SettingsProvider GetSettingsProvider<T>() where T : Settings<T>
        {
            var instanceProp = typeof( Settings<T> ).GetProperty( nameof( Settings<T>.instance ), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
            return new ScriptableObjectSettingsProvider( () => ( ScriptableObject )instanceProp.GetValue( null ),
                Settings<T>.attribute is EditorUserSettingsAttribute ?
                SettingsScope.User : SettingsScope.Project,
                Settings<T>.displayPath );
        }

        /// <summary>
        /// Creates a SettingsProvider for the given settings instance.
        /// Easy to copy-paste usage: <code>SettingsExtensions.GetSettingsProvider(() => instance)</code>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="instanceGetter"></param>
        /// <returns></returns>
        public static SettingsProvider GetSettingsProvider<T>(Func<T> instanceGetter)
            where T : Settings<T>
        {
            return new ScriptableObjectSettingsProvider( instanceGetter,
                Settings<T>.attribute is EditorUserSettingsAttribute ?
                SettingsScope.User : SettingsScope.Project,
                Settings<T>.displayPath );
        }
    }
}
