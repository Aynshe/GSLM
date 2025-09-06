namespace GameStoreLibraryManager.Steam.ValveKeyValue
{
    class KVToken
    {
        public KVToken(KVTokenType type)
            : this(type, null)
        {
        }

        public KVToken(KVTokenType type, string value)
        {
            TokenType = type;
            Value = value;
        }

        public KVTokenType TokenType { get; private set; }

        public string Value { get; private set; }
    }
}
