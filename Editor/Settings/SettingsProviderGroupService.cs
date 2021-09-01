using UnityEngine;
using UnityEditor;
using System.Reflection;
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Compilation;

namespace Hextant.Editor
{
    public static class SettingsProviderGroupService
    {
        private static bool _isInited;
        private static (TypeInfo Type, Attribute Attribute)[] _settingsTypes;

        [RuntimeInitializeOnLoadMethod( RuntimeInitializeLoadType.AfterAssembliesLoaded )]
        private static void Init()
        {
            if( _isInited )
                return;

            _isInited = true;

            Debug.Log( "GET TYPES" );

            var attributeAssemblyName = typeof( SettingsAttributeBase ).Assembly.FullName;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var inspectedAssemblies = assemblies
                .Where( a => a.GetReferencedAssemblies().Any( ra => ra.FullName == attributeAssemblyName ) )
                .ToArray();
            Debug.Log( "INSPECTED ASSEMBLIES: " + inspectedAssemblies.Length );
            _settingsTypes = inspectedAssemblies
                .SelectMany( a => a.DefinedTypes.Select( t => (Type: t, Attribute: t.GetCustomAttribute( typeof( SettingsAttributeBase ), true )) ).Where( ta => ta.Attribute != null ) )
                .ToArray();
            Debug.Log( "FOUND TYPES: " + string.Join( ", ", _settingsTypes.AsEnumerable() ) );
        }

        [SettingsProviderGroup]
        public static SettingsProvider[] CreateProviders()
        {
            Init();

            var result = new List<SettingsProvider>();

            foreach( var at in _settingsTypes )
            {
                if( at.Type.GetMethods( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic ).Any( mi => mi.GetCustomAttribute<SettingsProviderAttribute>() != null ) )
                {
                    Debug.Log( "SKIPPING " + at.Type );
                    continue;
                }

                if(typeof(ISettings).IsAssignableFrom(at.Type) == false)
                {
                    Debug.LogError( $"{at.Type} is decorated with {at.Attribute.GetType()} but does not inherit from Settings<>!\nPlease remove the attribute or fix the inheritance to e.g. Settings<{at.Type}>." );
                    continue;
                }

                var instanceProp = at.Type.GetProperty( "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );
                var displayPathProp = at.Type.GetProperty( "displayPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy );

                Debug.Log( $"ADDING {at.Type} with {instanceProp} and {displayPathProp}" );

                result.Add( new ScriptableObjectSettingsProvider( () => ( ScriptableObject )instanceProp.GetValue( null ),
                    at.Attribute is EditorUserSettingsAttribute ?
                    SettingsScope.User : SettingsScope.Project,
                    (string)displayPathProp.GetValue( null ) ) );
            }

            return result.ToArray();
        }
    }
}
