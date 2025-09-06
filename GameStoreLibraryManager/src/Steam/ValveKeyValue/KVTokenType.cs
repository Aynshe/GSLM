namespace GameStoreLibraryManager.Steam.ValveKeyValue
{
    enum KVTokenType
    {
        ObjectStart,
        ObjectEnd,
        String,
        EndOfFile,
        Comment,
        Condition,
        IncludeAndAppend,
        IncludeAndMerge
    }
}
