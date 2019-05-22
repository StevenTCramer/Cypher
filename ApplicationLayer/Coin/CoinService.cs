﻿// Cypher (c) by Tangram Inc
//
// Cypher is licensed under a
// Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International License.
//
// You should have received a copy of the license along with this
// work. If not, see <http://creativecommons.org/licenses/by-nc-nd/4.0/>.

using System;
using System.Security;
using TangramCypher.Helper;
using TangramCypher.Helper.LibSodium;
using TangramCypher.ApplicationLayer.Actor;
using Newtonsoft.Json;
using Secp256k1_ZKP.Net;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TangramCypher.ApplicationLayer.Wallet;
using Dawn;
using Microsoft.Extensions.Logging;

namespace TangramCypher.ApplicationLayer.Coin
{
    public class CoinService : ICoinService
    {
        public const int Tan = 1;
        public const int MicroTan = 100;
        public const int NanoTan = 1000_000_000;
        public const long AttoTan = 1000_000_000_000_000_000;

        private readonly ILogger logger;
        private string stamp;
        private int version;
        private SecureString password;
        private CoinDto mintedCoin;
        private ProofStruct proofStruct;
        private TransactionCoin transactionCoin;

        public CoinService(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Builds the receiver.
        /// </summary>
        /// <returns>The receiver.</returns>
        public CoinService BuildReceiver()
        {
            using (var secp256k1 = new Secp256k1())
            using (var pedersen = new Pedersen())
            using (var rangeProof = new RangeProof())
            {
                Stamp(NewStamp());
                Version(-1);

                MakeSingleCoin();

                var naTInput = NaT(TransactionCoin().Input);
                var blind = DeriveKey(naTInput, Stamp(), Coin().Version);

                byte[] blindSum = new byte[32];

                try
                {
                    blindSum = pedersen.BlindSum(new List<byte[]> { blind }, new List<byte[]> { });
                }
                catch (Exception ex)
                {
                    logger.LogError($"Message: {ex.Message}\n Stack: {ex.StackTrace}");
                    throw ex;
                }

                var commitPos = Commit(naTInput, blind);
                var commitSum = pedersen.CommitSum(new List<byte[]> { commitPos }, new List<byte[]> { });

                AttachEnvelope(secp256k1, pedersen, rangeProof, blindSum, commitSum, TransactionCoin().Input);

                TransactionCoin().Blind = blindSum.ToHex();
            }

            return this;
        }

        /// <summary>
        /// Builds the sender.
        /// </summary>
        /// <returns>The sender.</returns>
        public CoinService BuildSender()
        {
            using (var secp256k1 = new Secp256k1())
            using (var pedersen = new Pedersen())
            using (var rangeProof = new RangeProof())
            {
                Stamp(TransactionCoin().Stamp);
                Version(TransactionCoin().Version);

                MakeSingleCoin();

                var received = TransactionCoin().Chain.FirstOrDefault(tx => tx.TransactionType == TransactionType.Receive);

                var blindNeg = DeriveKey(TransactionCoin().Input, received.Stamp, Coin().Version);
                var commitNeg = pedersen.Commit(NaT(transactionCoin.Input), blindNeg);

                var commitNegs = TransactionCoin().Chain
                               .Where(tx => tx.TransactionType == TransactionType.Send)
                               .Select(c => pedersen.Commit(NaT(c.Amount), DeriveKey(c.Amount, c.Stamp, c.Version))).ToList();

                commitNegs.Add(commitNeg);

                var blindNegSums = TransactionCoin().Chain
                                .Where(tx => tx.TransactionType == TransactionType.Send)
                                .Select(c => DeriveKey(c.Amount, c.Stamp, c.Version)).ToList();

                blindNegSums.Add(blindNeg);

                var blindSum = pedersen.BlindSum(new List<byte[]> { received.Blind.FromHex() }, blindNegSums);
                var commitSum = pedersen.CommitSum(new List<byte[]> { received.Commitment.FromHex() }, commitNegs);

                AttachEnvelope(secp256k1, pedersen, rangeProof, blindSum, commitSum, TransactionCoin().Output);
            }

            return this;
        }

        /// <summary>
        /// Clears the change, imputs, minted coin, outputs, password, receiver output, stamp and version cache.
        /// </summary>
        public void ClearCache()
        {
            mintedCoin = null;
            password = null;
            stamp = null;
            version = 0;
            transactionCoin = null;
        }

        /// <summary>
        /// Commit the specified amount, version, stamp and password.
        /// </summary>
        /// <returns>The commit.</returns>
        /// <param name="amount">Amount.</param>
        /// <param name="version">Version.</param>
        /// <param name="stamp">Stamp.</param>
        /// <param name="password">Password.</param>
        public byte[] Commit(ulong amount, int version, string stamp, SecureString password)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();

            using (var pedersen = new Pedersen())
            {
                var blind = DeriveKey(version, stamp, password).FromHex();
                var commit = pedersen.Commit(amount, blind);

                return commit;
            }
        }

        /// <summary>
        /// Commit the specified amount.
        /// </summary>
        /// <returns>The commit.</returns>
        /// <param name="amount">Amount.</param>
        public byte[] Commit(ulong amount)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();

            using (var pedersen = new Pedersen())
            {
                var blind = DeriveKey(Version(), Stamp(), Password()).FromHex();
                var commit = pedersen.Commit(amount, blind);

                return commit;
            }
        }

        /// <summary>
        /// Commit the specified amount and blind.
        /// </summary>
        /// <returns>The commit.</returns>
        /// <param name="amount">Amount.</param>
        /// <param name="blind">Blind.</param>
        public byte[] Commit(ulong amount, byte[] blind)
        {
            Guard.Argument(blind, nameof(blind)).NotNull().MaxCount(32);

            byte[] commit = new byte[33];

            using (var pedersen = new Pedersen())
            {
                try
                {
                    commit = pedersen.Commit(amount, blind);
                }
                catch (Exception ex)
                {
                    logger.LogError($"Message: {ex.Message}\n Stack: {ex.StackTrace}");
                    throw ex;
                }

                return commit;
            }
        }

        /// <summary>
        /// Coin.
        /// </summary>
        /// <returns>The coin.</returns>
        public CoinDto Coin() => mintedCoin;

        /// <summary>
        /// Derives the coin.
        /// </summary>
        /// <returns>The coin.</returns>
        /// <param name="password">Password.</param>
        /// <param name="coin">Coin.</param>
        public CoinDto DeriveCoin(SecureString password, CoinDto coin)
        {
            Guard.Argument(password, nameof(password)).NotNull();
            Guard.Argument(coin, nameof(coin)).NotNull();

            var v0 = +coin.Version;
            var v1 = +coin.Version + 1;
            var v2 = +coin.Version + 2;

            var coinDto = new CoinDto()
            {
                Keeper = DeriveKey(v1, coin.Stamp, DeriveKey(v2, coin.Stamp, DeriveKey(v2, coin.Stamp, password).ToSecureString()).ToSecureString()),
                Version = v0,
                Principle = DeriveKey(v0, coin.Stamp, password),
                Stamp = coin.Stamp,
                Envelope = coin.Envelope,
                Hint = DeriveKey(v1, coin.Stamp, DeriveKey(v1, coin.Stamp, password).ToSecureString())
            };

            return coinDto;
        }

        /// <summary>
        /// Derives the coin.
        /// </summary>
        /// <returns>The coin.</returns>
        /// <param name="coin">Coin.</param>
        public CoinDto DeriveCoin(CoinDto coin)
        {
            Guard.Argument(password, nameof(password)).NotNull();
            Guard.Argument(coin, nameof(coin)).NotNull();

            var v0 = +coin.Version;
            var v1 = +coin.Version + 1;
            var v2 = +coin.Version + 2;

            var c = new CoinDto()
            {
                Keeper = DeriveKey(v1, coin.Stamp, DeriveKey(v2, coin.Stamp, DeriveKey(v2, coin.Stamp, Password()).ToSecureString()).ToSecureString()),
                Version = v0,
                Principle = DeriveKey(v0, coin.Stamp, Password()),
                Stamp = coin.Stamp,
                Envelope = coin.Envelope,
                Hint = DeriveKey(v1, coin.Stamp, DeriveKey(v1, coin.Stamp, Password()).ToSecureString())
            };

            return c;
        }

        /// <summary>
        /// Derives the key.
        /// </summary>
        /// <returns>The key.</returns>
        /// <param name="version">Version.</param>
        /// <param name="stamp">Stamp.</param>
        /// <param name="password">Password.</param>
        /// <param name="bytes">Bytes.</param>
        public string DeriveKey(int version, string stamp, SecureString password, int bytes = 32)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();

            using (var insecurePassword = password.Insecure())
            {
                return Cryptography.GenericHashNoKey($"{version} {stamp} {insecurePassword.Value}", bytes).ToHex();
            }
        }

        public byte[] DeriveKey(double amount, string stamp, int version)
        {
            Guard.Argument(amount, nameof(amount)).NotNegative();
            Guard.Argument(password, nameof(password)).NotNull();

            using (var insecurePassword = Password().Insecure())
            {
                return Cryptography.GenericHashNoKey($"{amount} {stamp} {version} {insecurePassword.Value}", 32);
            }
        }

        /// <summary>
        /// Gets the new stamp.
        /// </summary>
        /// <returns>The new stamp.</returns>
        public string NewStamp()
        {
            return Cryptography.GenericHashNoKey(Cryptography.RandomKey()).ToHex();
        }

        /// <summary>
        /// Hash the specified coin.
        /// </summary>
        /// <returns>The hash.</returns>
        /// <param name="coin">Coin.</param>
        public byte[] Hash(CoinDto coin)
        {
            Guard.Argument(coin, nameof(coin)).NotNull();

            return Cryptography.GenericHashNoKey(
                string.Format("{0} {1} {2} {3} {4} {5} {6}",
                    coin.Envelope.Commitment,
                    coin.Envelope.Proof,
                    coin.Envelope.PublicKey,
                    coin.Hint,
                    coin.Keeper,
                    coin.Principle,
                    coin.Stamp));
        }

        /// <summary>
        ///  Releases two secret keys to continue hashchaing for sender/recipient.
        /// </summary>
        /// <returns>The release.</returns>
        /// <param name="version">Version.</param>
        /// <param name="stamp">Stamp.</param>
        /// <param name="password">Password.</param>
        public (string, string) HotRelease(int version, string stamp, SecureString password)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();

            var key1 = DeriveKey(version + 1, stamp, password);
            var key2 = DeriveKey(version + 2, stamp, password);

            return (key1, key2);
        }

        /// <summary>
        /// Partial release one secret key for escrow.
        /// </summary>
        /// <returns>The release.</returns>
        /// <param name="version">Version.</param>
        /// <param name="stamp">Stamp.</param>
        /// <param name="memo">Memo.</param>
        /// <param name="password">Password.</param>
        public string PartialRelease(int version, string stamp, string memo, SecureString password)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();

            var subKey1 = DeriveKey(version + 1, stamp, password);
            var subKey2 = DeriveKey(version + 2, stamp, password).ToSecureString();
            var mix = DeriveKey(version + 2, stamp, subKey2);
            var redemption = new RedemptionKeyDto() { Key1 = subKey1, Key2 = mix, Memo = memo, Stamp = stamp };

            return JsonConvert.SerializeObject(redemption);
        }

        /// <summary>
        /// Change ownership.
        /// </summary>
        /// <returns>The swap.</returns>
        /// <param name="password">Password.</param>
        /// <param name="coin">Coin.</param>
        /// <param name="redemptionKey">Redemption key.</param>
        public (CoinDto, CoinDto) CoinSwap(SecureString password, CoinDto coin, RedemptionKeyDto redemptionKey)
        {
            Guard.Argument(password, nameof(password)).NotNull();
            Guard.Argument(coin, nameof(coin)).NotNull();
            Guard.Argument(redemptionKey, nameof(redemptionKey)).NotNull();

            try
            { coin = coin.FormatCoinFromBase64(); }
            catch (FormatException) { }

            if (!redemptionKey.Stamp.Equals(coin.Stamp))
                throw new Exception("Redemption stamp is not equal to the coins stamp!");

            var v1 = coin.Version + 1;
            var v2 = coin.Version + 2;
            var v3 = coin.Version + 3;
            var v4 = coin.Version + 4;

            var c1 = new CoinDto()
            {
                Keeper = DeriveKey(v2, redemptionKey.Stamp, DeriveKey(v3, redemptionKey.Stamp, DeriveKey(v3, redemptionKey.Stamp, password).ToSecureString()).ToSecureString()),
                Version = v1,
                Principle = redemptionKey.Key1,
                Stamp = redemptionKey.Stamp,
                Envelope = coin.Envelope,
                Hint = DeriveKey(v2, redemptionKey.Stamp, redemptionKey.Key2.ToSecureString())
            };

            c1.Hash = Hash(c1).ToHex();

            var c2 = new CoinDto()
            {
                Keeper = DeriveKey(v3, redemptionKey.Stamp, DeriveKey(v4, redemptionKey.Stamp, DeriveKey(v4, redemptionKey.Stamp, password).ToSecureString()).ToSecureString()),
                Version = v2,
                Principle = redemptionKey.Key2,
                Stamp = redemptionKey.Stamp,
                Envelope = coin.Envelope,
                Hint = DeriveKey(v3, redemptionKey.Stamp, DeriveKey(v3, redemptionKey.Stamp, password).ToSecureString())
            };

            c2.Hash = Hash(c2).ToHex();

            return (c1, c2);
        }

        /// <summary>
        /// Change partial ownership.
        /// </summary>
        /// <returns>The partial one.</returns>
        /// <param name="password">Password.</param>
        /// <param name="coin">Coin.</param>
        /// <param name="redemptionKey">Redemption key.</param>
        public CoinDto SwapPartialOne(SecureString password, CoinDto coin, RedemptionKeyDto redemptionKey)
        {
            Guard.Argument(password, nameof(password)).NotNull();
            Guard.Argument(coin, nameof(coin)).NotNull();
            Guard.Argument(redemptionKey, nameof(redemptionKey)).NotNull();

            var v1 = coin.Version + 1;
            var v2 = coin.Version + 2;
            var v3 = coin.Version + 3;

            coin.Keeper = DeriveKey(v2, coin.Stamp, DeriveKey(v3, coin.Stamp, DeriveKey(v3, coin.Stamp, password).ToSecureString()).ToSecureString());
            coin.Version = v1;
            coin.Principle = redemptionKey.Key1;
            coin.Stamp = coin.Stamp;
            coin.Envelope = coin.Envelope;
            coin.Hint = redemptionKey.Key2;

            return coin;
        }

        /// <summary>
        /// Sign the specified amount, version, stamp, password and msg.
        /// </summary>
        /// <returns>The sign.</returns>
        /// <param name="amount">Amount.</param>
        /// <param name="version">Version.</param>
        /// <param name="stamp">Stamp.</param>
        /// <param name="password">Password.</param>
        /// <param name="msg">Message.</param>
        public byte[] Sign(ulong amount, int version, string stamp, SecureString password, byte[] msg)
        {
            Guard.Argument(stamp, nameof(stamp)).NotNull().NotEmpty();
            Guard.Argument(password, nameof(password)).NotNull();
            Guard.Argument(msg, nameof(msg)).NotNull().MaxCount(32);

            using (var secp256k1 = new Secp256k1())
            {
                var blind = DeriveKey(version, stamp, password).FromHex();
                var sig = secp256k1.Sign(msg, blind);

                return sig;
            }
        }

        /// <summary>
        /// Sign the specified amount and msg.
        /// </summary>
        /// <returns>The sign.</returns>
        /// <param name="amount">Amount.</param>
        /// <param name="msg">Message.</param>
        public byte[] Sign(ulong amount, byte[] msg)
        {
            Guard.Argument(msg, nameof(msg)).NotNull().MaxCount(32);

            using (var secp256k1 = new Secp256k1())
            {
                var blind = DeriveKey(Version(), Stamp(), Password()).FromHex();
                var msgHash = Cryptography.GenericHashNoKey(Encoding.UTF8.GetString(msg));
                var sig = secp256k1.Sign(msgHash, blind);

                return sig;
            }
        }

        /// <summary>
        /// Signs with blinding factor.
        /// </summary>
        /// <returns>The with blinding.</returns>
        /// <param name="msg">Message.</param>
        /// <param name="blinding">Blinding.</param>
        public byte[] SignWithBlinding(byte[] msg, byte[] blinding)
        {
            Guard.Argument(msg, nameof(msg)).NotNull().MaxCount(32);
            Guard.Argument(blinding, nameof(blinding)).NotNull().MaxCount(32);

            using (var secp256k1 = new Secp256k1())
            {
                var msgHash = Cryptography.GenericHashNoKey(Encoding.UTF8.GetString(msg));
                return secp256k1.Sign(msgHash, blinding);
            }
        }

        //TODO.. possibly remove?
        /// <summary>
        /// Split coin.
        /// </summary>
        /// <returns>The split.</returns>
        /// <param name="blinding">Blinding.</param>
        public (byte[], byte[]) Split(byte[] blinding)
        {
            Guard.Argument(blinding, nameof(blinding)).NotNull().MaxCount(32);

            using (var pedersen = new Pedersen())
            {
                var skey1 = DeriveKey(transactionCoin.Input, stamp, version);

                byte[] skey2 = new byte[32];

                try
                {
                    skey2 = pedersen.BlindSum(new List<byte[]> { blinding }, new List<byte[]> { skey1 });
                }
                catch (Exception ex)
                {
                    logger.LogError($"Message: {ex.Message}\n Stack: {ex.StackTrace}");
                    throw ex;
                }

                return (skey1, skey2);
            }
        }

        /// <summary>
        /// Make a single coin.
        /// </summary>
        /// <returns>The single coin.</returns>
        /// <param name="transaction">Transaction.</param>
        /// <param name="password">Password.</param>
        public CoinDto MakeSingleCoin(TransactionDto transaction, SecureString password)
        {
            Guard.Argument(transaction, nameof(transaction)).NotNull();
            Guard.Argument(password, nameof(password)).NotNull();

            return DeriveCoin(password,
                new CoinDto
                {
                    Version = version,
                    Stamp = stamp,
                    Envelope = new EnvelopeDto()
                });
        }

        /// <summary>
        /// Makes the single coin.
        /// </summary>
        /// <returns>The single coin.</returns>
        public void MakeSingleCoin()
        {
            Guard.Argument(password, nameof(password)).NotNull();

            mintedCoin = DeriveCoin(new CoinDto
            {
                Version = version + 1,
                Stamp = stamp,
                Envelope = new EnvelopeDto()
            });
        }

        /// <summary>
        /// Makes multiple coins.
        /// </summary>
        /// <returns>The multiple coins.</returns>
        /// <param name="transactions">Transactions.</param>
        /// <param name="password">Password.</param>
        public IEnumerable<CoinDto> MakeMultipleCoins(IEnumerable<TransactionDto> transactions, SecureString password)
        {
            Guard.Argument(transactions, nameof(transactions)).NotNull();
            Guard.Argument(password, nameof(password)).NotNull();

            return transactions.Select(tx =>
                DeriveCoin(password, new CoinDto
                {
                    Version = tx.Version,
                    Stamp = tx.Stamp,
                    Envelope = new EnvelopeDto()
                }));
        }

        /// <summary>
        /// Range proof struct.
        /// </summary>
        /// <returns>The struct.</returns>
        public ProofStruct ProofStruct() => proofStruct;

        /// <summary>
        /// Verifies the coin on ownership.
        /// </summary>
        /// <returns>The coin.</returns>
        /// <param name="terminal">Terminal.</param>
        /// <param name="current">Current.</param>
        public int VerifyCoin(CoinDto terminal, CoinDto current)
        {
            Guard.Argument(terminal, nameof(terminal)).NotNull();
            Guard.Argument(current, nameof(current)).NotNull();

            return terminal.Keeper.Equals(current.Keeper) && terminal.Hint.Equals(current.Hint)
               ? 1
               : terminal.Hint.Equals(current.Hint)
               ? 2
               : terminal.Keeper.Equals(current.Keeper)
               ? 3
               : 4;
        }

        /// <summary>
        /// Version this instance.
        /// </summary>
        /// <returns>The version.</returns>
        public int Version() => version;

        /// <summary>
        /// Version the specified version.
        /// </summary>
        /// <returns>The version.</returns>
        /// <param name="version">Version.</param>
        public CoinService Version(int version)
        {
            this.version = version;
            return this;
        }

        /// <summary>
        /// Stamp this instance.
        /// </summary>
        /// <returns>The stamp.</returns>
        public string Stamp() => stamp;

        /// <summary>
        /// Stamp the specified stamp.
        /// </summary>
        /// <returns>The stamp.</returns>
        /// <param name="stamp">Stamp.</param>
        public CoinService Stamp(string stamp)
        {
            this.stamp = stamp;
            return this;
        }

        public TransactionCoin TransactionCoin() => transactionCoin;

        public CoinService TransactionCoin(TransactionCoin transactionCoin)
        {
            this.transactionCoin = transactionCoin;
            return this;
        }

        /// <summary>
        /// Password this instance.
        /// </summary>
        /// <returns>The password.</returns>
        public SecureString Password() => password;

        /// <summary>
        /// Password the specified password.
        /// </summary>
        /// <returns>The password.</returns>
        /// <param name="password">Password.</param>
        public CoinService Password(SecureString password)
        {
            this.password = password;
            return this;
        }

        /// <summary>
        /// naT decimal format.
        /// </summary>
        /// <returns>The t.</returns>
        /// <param name="value">Value.</param>
        private ulong NaT(double value)
        {
            return (ulong)(value * NanoTan);
        }

        /// <summary>
        /// Attaches the envelope.
        /// </summary>
        /// <param name="secp256k1">Secp256k1.</param>
        /// <param name="pedersen">Pedersen.</param>
        /// <param name="rangeProof">Range proof.</param>
        /// <param name="blindSum">Blind sum.</param>
        /// <param name="commitSum">Commit sum.</param>
        private void AttachEnvelope(Secp256k1 secp256k1, Pedersen pedersen, RangeProof rangeProof, byte[] blindSum, byte[] commitSum, double blance)
        {
            var (k1, k2) = Split(blindSum);

            Coin().Envelope.Commitment = commitSum.ToHex();
            Coin().Envelope.Proof = k2.ToHex();
            Coin().Envelope.PublicKey = pedersen.ToPublicKey(Commit(0, k1)).ToHex();
            Coin().Hash = Hash(Coin()).ToHex();
            Coin().Envelope.Signature = secp256k1.Sign(Coin().Hash.FromHex(), k1).ToHex();

            proofStruct = rangeProof.Proof(0, NaT(blance), blindSum, commitSum, Coin().Hash.FromHex());

            var isVerified = rangeProof.Verify(commitSum, proofStruct);

            if (!isVerified)
                throw new ArgumentOutOfRangeException(nameof(isVerified), "Range proof failed.");
        }
    }
}
