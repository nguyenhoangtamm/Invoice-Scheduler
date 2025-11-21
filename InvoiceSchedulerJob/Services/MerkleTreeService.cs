using Nethereum.Util;
using System.Security.Cryptography;
using System.Text;

namespace InvoiceSchedulerJob.Services;

public class MerkleTreeService
{
    private readonly ILogger<MerkleTreeService> _logger;

    public MerkleTreeService(ILogger<MerkleTreeService> logger)
    {
        _logger = logger;
    }

    public MerkleTreeResult BuildTree(IEnumerable<string> leaves)
    {
        var leafList = leaves.ToList();
        if (!leafList.Any())
        {
            throw new ArgumentException("Cannot build Merkle tree with empty leaves");
        }

        _logger.LogDebug("Building Merkle tree with {LeafCount} leaves", leafList.Count);

        // Sort leaves for deterministic tree construction
        var sortedLeaves = leafList.OrderBy(x => x).ToList();
        var leafHashes = sortedLeaves.Select(leaf => ComputeKeccakHash(leaf)).ToList();

        var proofs = new Dictionary<string, List<string>>();
        var tree = new List<List<string>> { leafHashes };

        // Build tree levels
        var currentLevel = leafHashes;
        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<string>();

            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                string left = currentLevel[i];
                string right = i + 1 < currentLevel.Count ? currentLevel[i + 1] : left;

                byte[] leftBytes = HexToBytes(left);
                byte[] rightBytes = HexToBytes(right);

                // Sắp xếp 2 node theo byte-wise để khớp với OpenZeppelin MerkleProof.processProof
                byte[] first, second;
                if (CompareBytes(leftBytes, rightBytes) <= 0)
                {
                    first = leftBytes;
                    second = rightBytes;
                }
                else
                {
                    first = rightBytes;
                    second = leftBytes;
                }

                byte[] combinedBytes = first.Concat(second).ToArray();
                string combinedHash = ComputeHashFromBytes(combinedBytes);
                nextLevel.Add(combinedHash);
            }

            tree.Add(nextLevel);
            currentLevel = nextLevel;
        }

        string merkleRoot = currentLevel[0];

        // Generate proofs for each leaf
        for (int leafIndex = 0; leafIndex < leafHashes.Count; leafIndex++)
        {
            var proof = GenerateProof(tree, leafIndex);
            var originalLeaf = sortedLeaves[leafIndex];
            proofs[originalLeaf] = proof;
        }

        _logger.LogInformation(
            "Built Merkle tree with root {MerkleRoot} for {LeafCount} leaves",
            merkleRoot, leafList.Count);

        return new MerkleTreeResult
        {
            Root = merkleRoot,
            Leaves = sortedLeaves,
            Proofs = proofs,
            TreeDepth = tree.Count - 1
        };
    }
    private int CompareBytes(byte[] a, byte[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            if (a[i] < b[i]) return -1;
            if (a[i] > b[i]) return 1;
        }
        if (a.Length < b.Length) return -1;
        if (a.Length > b.Length) return 1;
        return 0;
    }

    private static byte[] HexToBytes(string hex)
    {
        if (hex.StartsWith("0x")) hex = hex.Substring(2);
        return Convert.FromHexString(hex);
    }

    private static string ComputeHashFromBytes(byte[] bytes)
    {
        var keccak = new Sha3Keccack();
        var hashBytes = keccak.CalculateHash(bytes);
        return "0x" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
    private List<string> GenerateProof(List<List<string>> tree, int leafIndex)
    {
        var proof = new List<string>();
        int currentIndex = leafIndex;

        for (int level = 0; level < tree.Count - 1; level++)
        {
            var currentLevel = tree[level];

            // Determine sibling index
            int siblingIndex;
            if (currentIndex % 2 == 0)
            {
                // Current node is left child, sibling is right
                siblingIndex = currentIndex + 1;
            }
            else
            {
                // Current node is right child, sibling is left
                siblingIndex = currentIndex - 1;
            }

            // Add sibling to proof if it exists
            if (siblingIndex < currentLevel.Count)
            {
                proof.Add(currentLevel[siblingIndex]);
            }

            // Move to parent index for next level
            currentIndex = currentIndex / 2;
        }

        return proof;
    }

    public bool VerifyProof(string leaf, List<string> proof, string merkleRoot)
    {
        try
        {
            byte[] currentHash = HexToBytes(ComputeHash(leaf));

            foreach (string proofElement in proof)
            {
                byte[] proofBytes = HexToBytes(proofElement);

                // Determine order by comparing bytes (byte-wise) to match OpenZeppelin MerkleProof
                byte[] first, second;
                if (CompareBytes(currentHash, proofBytes) <= 0)
                {
                    first = currentHash;
                    second = proofBytes;
                }
                else
                {
                    first = proofBytes;
                    second = currentHash;
                }

                byte[] combinedBytes = first.Concat(second).ToArray();
                currentHash = HexToBytes(ComputeHashFromBytes(combinedBytes));
            }

            string computedRoot = "0x" + Convert.ToHexString(currentHash).ToLowerInvariant();
            bool isValid = computedRoot.Equals(merkleRoot, StringComparison.OrdinalIgnoreCase);

            _logger.LogDebug(
                "Merkle proof verification for leaf {Leaf}: {Result}",
                leaf, isValid ? "VALID" : "INVALID");

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Merkle proof for leaf {Leaf}", leaf);
            return false;
        }
    }

    private static string ComputeKeccakHash(string input)
    {
        var keccak = new Sha3Keccack();
        var hashBytes = keccak.CalculateHash(Encoding.UTF8.GetBytes(input));
        return "0x" + Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ComputeHash(string input)
    {
        // Use Keccak-256 for consistency with tree construction and blockchain verification
        return ComputeKeccakHash(input);
    }
}

public class MerkleTreeResult
{
    public string Root { get; set; } = string.Empty;
    public List<string> Leaves { get; set; } = new();
    public Dictionary<string, List<string>> Proofs { get; set; } = new();
    public int TreeDepth { get; set; }
}