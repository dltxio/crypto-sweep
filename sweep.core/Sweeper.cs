using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;

namespace sweep.core
{
    public class Sweeper
    {

        private readonly String _exPubKey;

        private NBXplorer.DerivationStrategy.DirectDerivationStrategy _expub;

        private readonly NBitcoin.Network _network;

        private readonly Uri _explorerURL;

        private readonly string _seed;

        private readonly INodeClient _nodeClient;

        public Sweeper(String exPubKey, NBitcoin.Network network, string explorerURL, string seed)
        {
            _exPubKey = exPubKey;
            _network = network;
            _explorerURL = new Uri(explorerURL);
            _seed = seed;
        }

        public async Task<String> SweepAll(IDestination address)
        {
            BitcoinExtPubKey root = new BitcoinExtPubKey(_exPubKey, _network);
            _expub = new NBXplorer.DerivationStrategy.DirectDerivationStrategy(root, true);

            return await Sweep(20, address);
        }

        private async Task<String> Sweep(Int32 feePerByte, IDestination address)
        {
            try
            {
                BitcoinExtPubKey root = new BitcoinExtPubKey(_exPubKey, _network);
                var expub = new NBXplorer.DerivationStrategy.DirectDerivationStrategy(root, true);

                NBXplorer.NBXplorerNetworkProvider provider = new NBXplorer.NBXplorerNetworkProvider(_network.ChainName);
                NBXplorer.NBXplorerNetwork xplorerNetwork = provider.GetFromCryptoCode("BTC");

                NBXplorer.ExplorerClient exClient = new NBXplorer.ExplorerClient(xplorerNetwork, _explorerURL);
                var utxos = await exClient.GetUTXOsAsync(expub);

                Console.WriteLine("Select change address...");

                var unused = await exClient.GetUnusedAsync(_expub, NBXplorer.DerivationStrategy.DerivationFeature.Change);
                var changeScriptPubKey = unused.Address; /// unused.ScriptPubKey; // BitcoinScriptAddress

                if (changeScriptPubKey == null)
                {
                    throw new ArgumentNullException();
                }

                // 3. Gather coins can be spend
                Console.WriteLine("Gathering unspent coins...");

                List<Coin> unspentCoins = new List<Coin>();
                HashSet<ISecret> signingKeys = new HashSet<ISecret>();

                Mnemonic mnemonic = new Mnemonic(_seed);
                // if (String.IsNullOrEmpty(password))
                // {
                //     mnemonic = new Mnemonic(_encryptedSeed);
                // }
                // else
                // {
                //     mnemonic = new Mnemonic(Models.Encryption.Decrypt(Convert.FromBase64String(_encryptedSeed), Convert.FromBase64String(password), _IV));
                // }

                ExtKey hdroot = mnemonic.DeriveExtKey();

                foreach (NBXplorer.Models.UTXO utxo in utxos.Confirmed.UTXOs.OrderByDescending(u => u.Confirmations))
                {
                    Console.WriteLine(utxo.KeyPath.ToString());
                    KeyPath keypath = new KeyPath("m/44'/0'/0'/" + utxo.KeyPath);

                    if (_network == Network.TestNet)
                    {
                        keypath = new KeyPath("m/44'/1'/0'/" + utxo.KeyPath);
                    }

                    ExtKey bitcoinSecret = hdroot.Derive(keypath).GetWif(_network);
                    System.Diagnostics.Debug.WriteLine(bitcoinSecret.ScriptPubKey.ToString());
                    System.Diagnostics.Debug.WriteLine(bitcoinSecret.GetPublicKey().WitHash.ScriptPubKey);
                    System.Diagnostics.Debug.WriteLine(bitcoinSecret.GetPublicKey().GetAddress(ScriptPubKeyType.Segwit, Network.TestNet).ToString());
                    Console.WriteLine(bitcoinSecret.GetPublicKey().GetAddress(ScriptPubKeyType.Segwit, _network).ToString());
                    signingKeys.Add(bitcoinSecret);
                    
                    Coin coin = utxo.AsCoin();
                    unspentCoins.Add(coin);
                    System.Diagnostics.Debug.WriteLine(coin.GetScriptCode());

                    // const Int64 buffer = 50000;
                    // if (unspentCoins.Sum(u => u.Amount.Satoshi) > requests.Sum(x => x.AmountToSend.Satoshi) + buffer)
                    // {
                    //     break;
                    // }
                }

                // 4. Get the fee
                Console.WriteLine("Calculating transaction fee...");
                Money fee = Money.Zero;

                // 5. How much money we can spend?
                Money availableAmount = unspentCoins.Sum(u => u.Amount.Satoshi);

                Console.WriteLine("Amount available {0}", availableAmount);

                // Not needed as we are sending all
                // Int64 sumToSendInSats = requests.Sum(x => x.AmountToSend.Satoshi);
                // Decimal sumToSendInBTC = requests.Sum(x => x.AmountToSend.Satoshi) / SATOSHIS;
                //Console.WriteLine("To send {0}", requests.Sum(x => x.AmountToSend));
                // Console.WriteLine("To send {0}", availableAmount);

                // 6. Do some checks
                if (availableAmount == Money.Zero)
                {
                    throw new Exception("Not enough coins.");
                }

                // 10. Build the transaction
                Console.WriteLine("Signing transaction...");

                var builder = _network.CreateTransactionBuilder()
                    .AddCoins(unspentCoins)
                    .AddKeys(signingKeys.ToArray());

                builder.Send(address, availableAmount);
                // foreach (SendRequest request in requests)
                // {
                //     builder.Send(address, availableAmount);
                // }

                var tx_est = builder.SetChange(changeScriptPubKey)
                    .BuildTransaction(true);

                int size = tx_est.GetSerializedSize();
                fee = new Money(size * feePerByte);

                NBitcoin.Transaction tx = builder
                    .SetChange(changeScriptPubKey)
                    .SendFees(fee)
                    .BuildTransaction(true);

                Boolean isValid = builder.Verify(tx);

                if (isValid)
                {
                    Console.WriteLine(tx.ToHex());
                    String result = tx.GetHash().ToString();
                    Console.WriteLine($"Transaction Id: {tx.GetHash()}");

                    var success = false;
                    var tried = 0;
                    var maxTry = 7;

                    do
                    {
                        tried++;
                        Console.WriteLine($"Try broadcasting transaction... ({tried})");

                        success = await _nodeClient.Broadcast(tx);
                        break;

                    } while (tried <= maxTry);

                    if (!success)
                    {
                        Console.WriteLine($"The transaction might not have been successfully broadcasted. Please check the Transaction ID in a block explorer.");
                    }

                    Console.WriteLine("Transaction is successfully propagated on the network.");
                    return result;
                }
                
                return "Invalid";
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                throw;
            }
        }
    }
}
