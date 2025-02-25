using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Signer;
using UnityEngine;
using System;
using Nethereum.Siwe.Core;
using System.Collections.Generic;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.ABI.EIP712;
using Nethereum.Signer.EIP712;
using Newtonsoft.Json.Linq;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;

namespace Thirdweb
{
    /// <summary>
    /// Connects and interacts with a wallet.
    /// </summary>
    public class Wallet : Routable
    {
        public Wallet()
            : base($"sdk{subSeparator}wallet") { }

        /// <summary>
        /// Connects a user's wallet via a given wallet provider.
        /// </summary>
        /// <param name="walletConnection">The wallet provider and optional parameters.</param>
        /// <returns>A task representing the connection result.</returns>
        public async Task<string> Connect(WalletConnection walletConnection)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.Connect(walletConnection);
            }
            else
            {
                return await ThirdwebManager.Instance.SDK.session.Connect(walletConnection);
            }
        }

        /// <summary>
        /// Disconnects the user's wallet.
        /// </summary>
        /// <returns>A task representing the disconnection process.</returns>
        public async Task Disconnect()
        {
            if (Utils.IsWebGLBuild())
            {
                await Bridge.Disconnect();
            }
            else
            {
                await ThirdwebManager.Instance.SDK.session.Disconnect();
            }
        }

        /// <summary>
        /// Encrypts and exports the local wallet as a password-protected JSON keystore.
        /// </summary>
        /// <param name="password">The password used to encrypt the keystore (optional).</param>
        /// <returns>The exported JSON keystore as a string.</returns>
        public async Task<string> Export(string password)
        {
            password = string.IsNullOrEmpty(password) ? SystemInfo.deviceUniqueIdentifier : password;

            if (Utils.IsWebGLBuild())
            {
                return await Bridge.ExportWallet(password);
            }
            else
            {
                var localAccount = ThirdwebManager.Instance.SDK.session.ActiveWallet.GetLocalAccount();
                if (localAccount == null)
                    throw new Exception("No local account found");
                return Utils.EncryptAndGenerateKeyStore(new EthECKey(localAccount.PrivateKey), password);
            }
        }

        /// <summary>
        /// Authenticates the user by signing a payload that can be used to securely identify users. See https://portal.thirdweb.com/auth.
        /// </summary>
        /// <param name="domain">The domain to authenticate to.</param>
        /// <returns>A task representing the authentication result.</returns>
        public async Task<LoginPayload> Authenticate(string domain)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<LoginPayload>($"auth{subSeparator}login", Utils.ToJsonStringArray(domain));
            }
            else
            {
                var siwe = ThirdwebManager.Instance.SDK.session.SiweSession;
                var siweMsg = new SiweMessage()
                {
                    Resources = new List<string>(),
                    Uri = $"https://{domain}",
                    Statement = "Please ensure that the domain above matches the URL of the current website.",
                    Address = await GetSignerAddress(),
                    Domain = domain,
                    ChainId = (await GetChainId()).ToString(),
                    Version = "1",
                    Nonce = null,
                    IssuedAt = null,
                    ExpirationTime = null,
                    NotBefore = null,
                    RequestId = null
                };
                siweMsg.SetIssuedAtNow();
                siweMsg.SetExpirationTime(DateTime.UtcNow.AddSeconds(60 * 5));
                siweMsg.SetNotBefore(DateTime.UtcNow);
                siweMsg = siwe.AssignNewNonce(siweMsg);

                var finalMsg = SiweMessageStringBuilder.BuildMessage(siweMsg);
                var signature = await Sign(finalMsg);

                return new LoginPayload()
                {
                    signature = signature,
                    payload = new LoginPayloadData()
                    {
                        domain = siweMsg.Domain,
                        address = siweMsg.Address,
                        statement = siweMsg.Statement,
                        uri = siweMsg.Uri,
                        version = siweMsg.Version,
                        chain_id = siweMsg.ChainId,
                        nonce = siweMsg.Nonce,
                        issued_at = siweMsg.IssuedAt,
                        expiration_time = siweMsg.ExpirationTime,
                        invalid_before = siweMsg.NotBefore,
                        resources = siweMsg.Resources,
                    }
                };
            }
        }

        /// <summary>
        /// Verifies the authenticity of a login payload.
        /// </summary>
        /// <param name="payload">The login payload to verify.</param>
        /// <returns>The verification result as a string.</returns>
        public async Task<string> Verify(LoginPayload payload)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<string>($"auth{subSeparator}verify", Utils.ToJsonStringArray(payload));
            }
            else
            {
                var siwe = ThirdwebManager.Instance.SDK.session.SiweSession;
                var siweMessage = new SiweMessage()
                {
                    Domain = payload.payload.domain,
                    Address = payload.payload.address,
                    Statement = payload.payload.statement,
                    Uri = payload.payload.uri,
                    Version = payload.payload.version,
                    ChainId = payload.payload.chain_id,
                    Nonce = payload.payload.nonce,
                    IssuedAt = payload.payload.issued_at,
                    ExpirationTime = payload.payload.expiration_time,
                    NotBefore = payload.payload.invalid_before,
                    Resources = payload.payload.resources,
                    RequestId = null
                };
                var signature = payload.signature;
                var validUser = await siwe.IsUserAddressRegistered(siweMessage);
                if (validUser)
                {
                    if (await siwe.IsMessageSignatureValid(siweMessage, signature))
                    {
                        if (siwe.IsMessageTheSameAsSessionStored(siweMessage))
                        {
                            if (siwe.HasMessageDateStartedAndNotExpired(siweMessage))
                            {
                                return siweMessage.Address;
                            }
                            else
                            {
                                return "Expired";
                            }
                        }
                        else
                        {
                            return "Invalid Session";
                        }
                    }
                    else
                    {
                        return "Invalid Signature";
                    }
                }
                else
                {
                    return "Invalid User";
                }
            }
        }

        /// <summary>
        /// Gets the balance of the connected wallet.
        /// </summary>
        /// <param name="currencyAddress">Optional address of the currency to check balance of.</param>
        /// <returns>The balance of the wallet as a CurrencyValue object.</returns>
        public async Task<CurrencyValue> GetBalance(string currencyAddress = Utils.NativeTokenAddress)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<CurrencyValue>(getRoute("balance"), Utils.ToJsonStringArray(currencyAddress));
            }
            else
            {
                if (!await IsConnected())
                    throw new Exception("No account connected!");

                if (currencyAddress != Utils.NativeTokenAddress)
                {
                    Contract contract = ThirdwebManager.Instance.SDK.GetContract(currencyAddress);
                    return await contract.ERC20.Balance();
                }
                else
                {
                    HexBigInteger balance = null;
                    string address = await GetAddress();
                    try
                    {
                        balance = await ThirdwebManager.Instance.SDK.session.Web3.Eth.GetBalance.SendRequestAsync(address);
                    }
                    catch
                    {
                        balance = await new Web3(ThirdwebManager.Instance.SDK.session.RPC).Eth.GetBalance.SendRequestAsync(address);
                    }
                    var nativeCurrency = ThirdwebManager.Instance.SDK.session.CurrentChainData.nativeCurrency;
                    return new CurrencyValue(nativeCurrency.name, nativeCurrency.symbol, nativeCurrency.decimals.ToString(), balance.Value.ToString(), balance.Value.ToString().ToEth());
                }
            }
        }

        /// <summary>
        /// Gets the connected wallet address.
        /// </summary>
        /// <returns>The address of the connected wallet as a string.</returns>
        public async Task<string> GetAddress()
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<string>(getRoute("getAddress"), new string[] { });
            }
            else
            {
                if (!await IsConnected())
                    throw new Exception("No account connected!");

                return await ThirdwebManager.Instance.SDK.session.ActiveWallet.GetAddress();
            }
        }

        /// <summary>
        /// Gets the address of the signer associated with the connected wallet.
        /// </summary>
        /// <returns>The address of the signer as a string.</returns>
        public async Task<string> GetSignerAddress()
        {
            if (Utils.IsWebGLBuild())
            {
                return await GetAddress();
            }
            else
            {
                if (!await IsConnected())
                    throw new Exception("No account connected!");

                return await ThirdwebManager.Instance.SDK.session.ActiveWallet.GetSignerAddress();
            }
        }

        /// <summary>
        /// Checks if a wallet is connected.
        /// </summary>
        /// <returns>True if a wallet is connected, false otherwise.</returns>
        public async Task<bool> IsConnected()
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<bool>(getRoute("isConnected"), new string[] { });
            }
            else
            {
                try
                {
                    return await ThirdwebManager.Instance.SDK.session.ActiveWallet.IsConnected();
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the connected chainId.
        /// </summary>
        /// <returns>The connected chainId as an integer.</returns>
        public async Task<int> GetChainId()
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<int>(getRoute("getChainId"), new string[] { });
            }
            else
            {
                var hexChainId = await ThirdwebManager.Instance.SDK.session.Request<string>("eth_chainId");
                return (int)hexChainId.HexToBigInteger(false);
            }
        }

        /// <summary>
        /// Prompts the connected wallet to switch to the given chainId.
        /// </summary>
        /// <param name="chainId">The chainId to switch to.</param>
        /// <returns>A task representing the switching process.</returns>
        public async Task SwitchNetwork(int chainId)
        {
            if (Utils.IsWebGLBuild())
            {
                await Bridge.SwitchNetwork(chainId);
            }
            else
            {
                throw new UnityException("This functionality is not yet available on your current platform.");
            }
        }

        /// <summary>
        /// Transfers currency to a given address.
        /// </summary>
        /// <param name="to">The address to transfer the currency to.</param>
        /// <param name="amount">The amount of currency to transfer.</param>
        /// <param name="currencyAddress">Optional address of the currency to transfer (defaults to native token address).</param>
        /// <returns>The result of the transfer as a TransactionResult object.</returns>
        public async Task<TransactionResult> Transfer(string to, string amount, string currencyAddress = Utils.NativeTokenAddress)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<TransactionResult>(getRoute("transfer"), Utils.ToJsonStringArray(to, amount, currencyAddress));
            }
            else
            {
                if (currencyAddress != Utils.NativeTokenAddress)
                {
                    Contract contract = ThirdwebManager.Instance.SDK.GetContract(currencyAddress);
                    return await contract.ERC20.Transfer(to, amount);
                }
                else
                {
                    var receipt = await ThirdwebManager.Instance.SDK.session.Web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(to, decimal.Parse(amount));
                    return receipt.ToTransactionResult();
                }
            }
        }

        /// <summary>
        /// Prompts the connected wallet to sign the given message.
        /// </summary>
        /// <param name="message">The message to sign.</param>
        /// <returns>The signature of the message as a string.</returns>
        public async Task<string> Sign(string message)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<string>(getRoute("sign"), Utils.ToJsonStringArray(message));
            }
            else
            {
                return await ThirdwebManager.Instance.SDK.session.Request<string>("personal_sign", message, await GetSignerAddress());
            }
        }

        /// <summary>
        /// Signs a typed data object using EIP-712 signature.
        /// </summary>
        /// <typeparam name="T">The type of the data to sign.</typeparam>
        /// <typeparam name="TDomain">The type of the domain object.</typeparam>
        /// <param name="data">The data object to sign.</param>
        /// <param name="typedData">The typed data object that defines the domain and message schema.</param>
        /// <returns>The signature of the typed data as a string.</returns>
        public async Task<string> SignTypedDataV4<T, TDomain>(T data, TypedData<TDomain> typedData)
            where TDomain : IDomain
        {
            if (ThirdwebManager.Instance.SDK.session.ActiveWallet.GetSignerProvider() == WalletProvider.LocalWallet)
            {
                var signer = new Eip712TypedDataSigner();
                var key = new EthECKey(ThirdwebManager.Instance.SDK.session.ActiveWallet.GetLocalAccount().PrivateKey);
                return signer.SignTypedDataV4(data, typedData, key);
            }
            else
            {
                var json = typedData.ToJson(data);
                var jsonObject = JObject.Parse(json);

                var uidToken = jsonObject.SelectToken("$.message.uid");
                if (uidToken != null)
                {
                    var uidBase64 = uidToken.Value<string>();
                    var uidBytes = Convert.FromBase64String(uidBase64);
                    var uidHex = uidBytes.ByteArrayToHexString();
                    uidToken.Replace(uidHex);
                }

                var messageObject = jsonObject.GetValue("message") as JObject;
                foreach (var property in messageObject.Properties())
                    property.Value = property.Value.ToString();

                string safeJson = jsonObject.ToString();
                return await ThirdwebManager.Instance.SDK.session.Request<string>("eth_signTypedData_v4", await GetSignerAddress(), safeJson);
            }
        }

        /// <summary>
        /// Recovers the original wallet address that signed a message.
        /// </summary>
        /// <param name="message">The message that was signed.</param>
        /// <param name="signature">The signature of the message.</param>
        /// <returns>The recovered wallet address as a string.</returns>
        public async Task<string> RecoverAddress(string message, string signature)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<string>(getRoute("recoverAddress"), Utils.ToJsonStringArray(message, signature));
            }
            else
            {
                var signer = new EthereumMessageSigner();
                var addressRecovered = signer.EncodeUTF8AndEcRecover(message, signature);
                return addressRecovered;
            }
        }

        /// <summary>
        /// Sends a raw transaction from the connected wallet.
        /// </summary>
        /// <param name="transactionRequest">The transaction request object containing transaction details.</param>
        /// <returns>The result of the transaction as a TransactionResult object.</returns>
        public async Task<TransactionResult> SendRawTransaction(TransactionRequest transactionRequest)
        {
            if (Utils.IsWebGLBuild())
            {
                return await Bridge.InvokeRoute<TransactionResult>(getRoute("sendRawTransaction"), Utils.ToJsonStringArray(transactionRequest));
            }
            else
            {
                var input = new Nethereum.RPC.Eth.DTOs.TransactionInput(
                    transactionRequest.data,
                    transactionRequest.to,
                    transactionRequest.from,
                    new Nethereum.Hex.HexTypes.HexBigInteger(BigInteger.Parse(transactionRequest.gasLimit)),
                    new Nethereum.Hex.HexTypes.HexBigInteger(BigInteger.Parse(transactionRequest.gasPrice)),
                    new Nethereum.Hex.HexTypes.HexBigInteger(transactionRequest.value)
                );
                var receipt = await ThirdwebManager.Instance.SDK.session.Web3.Eth.TransactionManager.SendTransactionAndWaitForReceiptAsync(input);
                return receipt.ToTransactionResult();
            }
        }

        /// <summary>
        /// Prompts the user to fund their wallet using one of the Thirdweb pay providers (defaults to Coinbase Pay).
        /// </summary>
        /// <param name="options">The options for funding the wallet.</param>
        /// <returns>A task representing the funding process.</returns>
        public async Task FundWallet(FundWalletOptions options)
        {
            if (Utils.IsWebGLBuild())
            {
                if (options.address == null)
                {
                    options.address = await GetAddress();
                }
                await Bridge.FundWallet(options);
            }
            else
            {
                throw new UnityException("This functionality is not yet available on your current platform.");
            }
        }
    }

    /// <summary>
    /// Represents the connection details for a wallet.
    /// </summary>
    public class WalletConnection
    {
        public WalletProvider provider;

        public BigInteger chainId;

        public string password;

        public string email;

        public WalletProvider personalWallet;

        /// <summary>
        /// Initializes a new instance of the <see cref="WalletConnection"/> class with the specified parameters.
        /// </summary>
        /// <param name="provider">The wallet provider to connect to.</param>
        /// <param name="chainId">The chain ID.</param>
        /// <param name="password">The wallet password if using local wallets.</param>
        /// <param name="email">The email to login with if using email based providers.</param>
        /// <param name="personalWallet">The personal wallet provider if using smart wallets.</param>
        public WalletConnection(WalletProvider provider, BigInteger chainId, string password = null, string email = null, WalletProvider personalWallet = WalletProvider.LocalWallet)
        {
            this.provider = provider;
            this.chainId = chainId;
            this.password = password;
            this.email = email;
            this.personalWallet = personalWallet;
        }
    }

    /// <summary>
    /// Represents the available wallet providers.
    /// </summary>
    [System.Serializable]
    public enum WalletProvider
    {
        Metamask,
        Coinbase,
        WalletConnect,
        Injected,
        MagicLink,
        LocalWallet,
        SmartWallet,
        Paper,
        Hyperplay
    }
}
