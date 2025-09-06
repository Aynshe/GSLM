using System;

namespace GameStoreLibraryManager.Steam.ValveKeyValue.Deserialization
{
    interface IVisitingReader : IDisposable
    {
        void ReadObject();
    }
}
