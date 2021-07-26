// Copyright 2021 by Hextant Studios. https://HextantStudios.com
// This work is licensed under CC BY 4.0. http://creativecommons.org/licenses/by/4.0/
using System;

namespace Hextant
{
    // Specifies the settings type, path in the settings UI, and optionally its
    // filename. If the filename is not set, the type's name is used.
    public abstract class SettingsAttribute : Attribute
    {
        internal SettingsAttribute( SettingsUsage usage, string displayPath = null)
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

    public class RuntimeProjectSettingsAttribute : SettingsAttribute, IRuntimeSettingsAttribute
    {
        public RuntimeProjectSettingsAttribute(string displayPath = null )
            : base(SettingsUsage.RuntimeProject, displayPath )
        { }

        public bool allowRuntimeFileOverrides { get; set; }
    }

    public class EditorProjectSettingsAttribute : SettingsAttribute
    {
        public EditorProjectSettingsAttribute( string displayPath = null )
            : base( SettingsUsage.EditorProject, displayPath )
        { }
    }

    public class EditorUserSettingsAttribute : SettingsAttribute
    {
        public EditorUserSettingsAttribute( string displayPath = null )
            : base( SettingsUsage.EditorUser, displayPath )
        { }
    }

    public interface IRuntimeSettingsAttribute
    {
        /// <summary>
        /// Set to true to try loading overrides from a <see>filename</see>.json placed in the working directory when entering runtime
        /// </summary>
        bool allowRuntimeFileOverrides { get; set; }
    }
}
