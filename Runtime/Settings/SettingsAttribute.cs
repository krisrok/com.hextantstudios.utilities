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

        public OverrideOptions OverrideOptions { get; set; }

        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead" )]
        public bool allowRuntimeFileOverrides { get => this.AllowsFileOverrides(); set => OverrideOptions |= OverrideOptions.File; }

        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead" )]
        public bool allowRuntimeFileWatchers { get => this.AllowsFileWatchers(); set => OverrideOptions |= OverrideOptions.FileWatcher; }

        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead" )]
        public bool allowCommandlineArgsOverrides { get => this.AllowsCommandlineOverrides(); set => OverrideOptions |= OverrideOptions.Commandline; }
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
        /// Set to true to try loading overrides from a <see cref="Settings{T}.filename">filename</see>.json placed in the working directory when entering runtime.
        /// Works in conjunction with <see cref="SerializableSettings{T}"/>
        /// </summary>
        ///
        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead." )]
        bool allowRuntimeFileOverrides { get; set; }

        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead." )]
        bool allowRuntimeFileWatchers { get; set; }

        [Obsolete( "Use " + nameof( OverrideOptions ) + " instead." )]
        bool allowCommandlineArgsOverrides { get; set; }

        OverrideOptions OverrideOptions { get; set; }
    }

    public static class RuntimeSettingsAttributeExtensions
    {
        public static bool AllowsFileOverrides( this IRuntimeSettingsAttribute attribute ) => ( attribute.OverrideOptions & OverrideOptions.File ) != 0;
        public static bool AllowsFileWatchers( this IRuntimeSettingsAttribute attribute ) => ( attribute.OverrideOptions & OverrideOptions.FileWatcher ) != 0;
        public static bool AllowsCommandlineOverrides( this IRuntimeSettingsAttribute attribute ) => ( attribute.OverrideOptions & OverrideOptions.Commandline ) != 0;
    }

    [Flags]
    public enum OverrideOptions
    {
        None = 0,
        File = 1,
        FileWatcher = File | 2,
        Commandline = 1 << 2,
    }
}
