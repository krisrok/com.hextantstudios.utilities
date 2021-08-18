// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;

namespace Hextant
{
    /// <summary>
    /// Abstract base class for Settings attributes.
    /// Please use its derivates <see cref="RuntimeProjectSettingsAttribute"/>, <see cref="EditorProjectSettingsAttribute"/> and <see cref="EditorUserSettingsAttribute"/> instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public abstract class SettingsAttributeBase : Attribute
    {
        internal SettingsAttributeBase( SettingsUsage usage, string displayPath = null)
        {
            this.usage = usage;
            this.displayPath = displayPath;
        }

        // The type of settings (how and when they are used).
        internal readonly SettingsUsage usage;

        // The display name and optional path in the settings dialog.
        public readonly string displayPath;

        // The filename used to store the settings. If null, the type's name is used.
        public readonly string filename;
    }

    public class RuntimeProjectSettingsAttribute : SettingsAttributeBase, IRuntimeSettingsAttribute
    {
        public RuntimeProjectSettingsAttribute(string displayPath = null )
            : base(SettingsUsage.RuntimeProject, displayPath )
        { }

        public bool allowRuntimeFileOverrides { get; set; }
        public bool allowRuntimeFileWatchers { get; set; }
    }

    public class EditorProjectSettingsAttribute : SettingsAttributeBase
    {
        public EditorProjectSettingsAttribute( string displayPath = null )
            : base( SettingsUsage.EditorProject, displayPath )
        { }
    }

    public class EditorUserSettingsAttribute : SettingsAttributeBase
    {
        public EditorUserSettingsAttribute( string displayPath = null )
            : base( SettingsUsage.EditorUser, displayPath )
        { }
    }

    public interface IRuntimeSettingsAttribute
    {
        /// <summary>
        /// Set to true to try loading overrides from a <see>filename</see>.json placed in the working directory when entering runtime.
        /// Works in conjunction with <see cref="SerializableSettings{T}"/>
        /// </summary>
        bool allowRuntimeFileOverrides { get; set; }
        bool allowRuntimeFileWatchers { get; set; }
    }
}
