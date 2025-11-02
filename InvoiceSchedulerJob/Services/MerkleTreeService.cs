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
        var leafHashes = sortedLeaves.Select(ComputeHash).ToList();

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

                string combinedHash = ComputeHash(left + right);
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
            string currentHash = ComputeHash(leaf);

            foreach (string proofElement in proof)
            {
                // Determine order by comparing hashes lexicographically
                if (string.Compare(currentHash, proofElement, StringComparison.Ordinal) < 0)
                {
                    currentHash = ComputeHash(currentHash + proofElement);
                }
                else
                {
                    currentHash = ComputeHash(proofElement + currentHash);
                }
            }

            bool isValid = currentHash.Equals(merkleRoot, StringComparison.OrdinalIgnoreCase);

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

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}

public class MerkleTreeResult
{
    public string Root { get; set; } = string.Empty;
    public List<string> Leaves { get; set; } = new();
    public Dictionary<string, List<string>> Proofs { get; set; } = new();
    public int TreeDepth { get; set; }
}