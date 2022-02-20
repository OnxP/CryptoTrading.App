namespace CryptoTrading.App.Process
{
    public interface IProcessManagement
    {
        public void Run(int retries);
    }
}