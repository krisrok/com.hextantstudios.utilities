using System.Runtime.CompilerServices;
using SerializableSettings;

[assembly: InternalsVisibleTo( "Hextant Utilities" )]

namespace Hextant
{
    public abstract class SerializableSettings<T> : SerializableSettings.SerializableSettings<T>
        where T : SerializableSettings<T>
    { }
}
