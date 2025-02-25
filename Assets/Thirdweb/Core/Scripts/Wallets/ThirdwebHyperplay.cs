using System.Threading.Tasks;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Thirdweb.Hyperplay;

namespace Thirdweb.Wallets
{
    public class ThirdwebHyperplay : IThirdwebWallet
    {
        private Web3 _web3;
        private WalletProvider _provider;
        private WalletProvider _signerProvider;
        private Hyperplay.Hyperplay _hyperPlay;

        public ThirdwebHyperplay(string chainId)
        {
            _web3 = null;
            _provider = WalletProvider.Hyperplay;
            _signerProvider = WalletProvider.Hyperplay;
            _hyperPlay = new Hyperplay.Hyperplay(chainId);
        }

        public async Task<string> Connect(WalletConnection walletConnection, string rpc)
        {
            await _hyperPlay.Initialize();
            _web3 = _hyperPlay.CreateWeb3();
            return _hyperPlay.Accounts[0];
        }

        public Task Disconnect()
        {
            _web3 = null;
            return Task.CompletedTask;
        }

        public Account GetLocalAccount()
        {
            return null;
        }

        public Task<string> GetAddress()
        {
            var addy = _hyperPlay.Accounts[0];
            if (addy != null)
                addy = addy.ToChecksumAddress();
            return Task.FromResult(addy);
        }

        public async Task<string> GetSignerAddress()
        {
            return await GetAddress();
        }

        public WalletProvider GetProvider()
        {
            return _provider;
        }

        public WalletProvider GetSignerProvider()
        {
            return _signerProvider;
        }

        public Task<Web3> GetWeb3()
        {
            return Task.FromResult(_web3);
        }

        public async Task<Web3> GetSignerWeb3()
        {
            return await GetWeb3();
        }

        public Task<bool> IsConnected()
        {
            return Task.FromResult(_web3 != null);
        }
    }
}
