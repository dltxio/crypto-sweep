using System;
using System.Threading.Tasks;

namespace sweep.core
{
    public interface INodeClient
    {
        Task<Boolean> Broadcast(NBitcoin.Transaction tx);
    }
}
