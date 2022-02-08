
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace sweep.core
{
    public class BlockCypherClient : INodeClient
    {
        private readonly NBitcoin.Network _network;

        public BlockCypherClient(NBitcoin.Network network)
        {
            _network = network;
        }

        public async Task<Boolean> Broadcast(NBitcoin.Transaction tx)
        {
            using (HttpClient client = new HttpClient())
            {
                //api.blockcypher.com/v1/btc/main
                Models.BlockCypher.PushTxRequest request = new Models.BlockCypher.PushTxRequest() { tx = tx.ToHex().ToUpper() };
                string url = _network == NBitcoin.Network.Main ? "https://api.blockcypher.com/v1/btc/main/txs/push" : "https://api.blockcypher.com/v1/btc/test3/txs/push";
                var txPushResponse = await client.PostAsJsonAsync<Models.BlockCypher.PushTxRequest>(url, request);

                return txPushResponse.IsSuccessStatusCode;
            }
        }
    }
}