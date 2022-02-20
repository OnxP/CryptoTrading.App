namespace CryptoTrading.App.Core.KeyClass
{
    public class Key : IKey
    {
        private string _key;

        public Key(string key)
        {
            _key = key;
        }
        public string KeyValue => _key;
    }
    public interface IKey
    {
        string KeyValue { get; }
    }
}
