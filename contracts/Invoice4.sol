// SPDX-License-Identifier: MIT
pragma solidity ^0.8.28;

import "@openzeppelin/contracts/access/AccessControl.sol";
import "@openzeppelin/contracts/access/Ownable2Step.sol";
import "@openzeppelin/contracts/utils/Pausable.sol";
import "@openzeppelin/contracts/utils/cryptography/MerkleProof.sol";

/// @title Invoice4 - Hybrid approach: Individual CIDs + Batch metadata URI
/// @notice Stores merkle root built from individual invoice CIDs + batch metadata URI for convenience
/// @dev Combines granular access (individual CIDs) with on-chain convenience (batch URI)
contract Invoice4 is AccessControl, Ownable2Step, Pausable {
    using MerkleProof for bytes32[];

    // ============ ROLES ============
    bytes32 public constant SERVER_ROLE = keccak256("SERVER_ROLE");

    // ============ STRUCTS ============
    /// @notice Represents an anchored invoice batch with hybrid storage approach
    struct InvoiceBatch {
        bytes32 merkleRoot;        // Root of Merkle tree (built from individual invoice CIDs)
        uint256 batchSize;         // Number of invoices in this batch
        address issuer;            // Address who issued the batch
        string metadataURI;        // IPFS URI pointing to batch index (contains CID list + proofs)
        uint256 timestamp;         // Block timestamp when anchored
    }

    // ============ STATE ============
    /// @notice Mapping of merkleRoot => InvoiceBatch details
    mapping(bytes32 => InvoiceBatch) public batches;

    /// @notice Array of all merkle roots for enumeration
    bytes32[] public batchRoots;

    /// @notice Counter for total batches anchored
    uint256 public totalBatches;

    /// @notice Counter for total invoices anchored across all batches
    uint256 public totalInvoices;

    // ============ EVENTS ============
    /// @notice Emitted when a new invoice batch is anchored
    event BatchAnchored(
        bytes32 indexed merkleRoot,
        uint256 indexed batchSize,
        address indexed issuer,
        string metadataURI,        // Batch index URI (contains individual CIDs)
        uint256 timestamp,
        uint256 batchIndex         // Index in batchRoots array
    );

    /// @notice Emitted when an invoice is verified in a batch
    event InvoiceVerified(
        bytes32 indexed merkleRoot,
        bytes32 indexed invoiceHash,
        address indexed verifier,
        string invoiceCID          // Individual invoice CID (if provided)
    );

    /// @notice Emitted when individual invoice CID is mapped for indexing
    /// @dev Can be emitted by off-chain services for better indexing
    event IndividualInvoiceRegistered(
        bytes32 indexed merkleRoot,
        bytes32 indexed invoiceHash,
        string indexed invoiceId,
        string invoiceCID
    );

    // ============ CONSTRUCTOR ============
    /// @notice Initialize contract with admin and initial server
    /// @param _initialServer Address of the initial authorized server
    constructor(address _initialServer) Ownable(msg.sender) {
        require(_initialServer != address(0), "invalid-server-address");
        
        _grantRole(DEFAULT_ADMIN_ROLE, msg.sender);
        _grantRole(SERVER_ROLE, _initialServer);
    }

    // ============ ANCHOR FUNCTIONS ============
    /// @notice Anchor a batch with hybrid approach: individual CIDs + batch metadata
    /// @param _merkleRoot Root of the merkle tree containing all invoice CID hashes
    /// @param _batchSize Number of invoices in this batch
    /// @param _metadataURI IPFS URI pointing to batch metadata (contains individual CIDs + proofs)
    /// @dev Batch metadata should contain: {"invoices": [{"id": "INV-001", "cid": "QmXXX", "hash": "0x...", "proof": [...]}]}
    function anchorBatch(
        bytes32 _merkleRoot,
        uint256 _batchSize,
        string calldata _metadataURI
    ) external onlyRole(SERVER_ROLE) whenNotPaused {
        require(_merkleRoot != bytes32(0), "invalid-merkle-root");
        require(_batchSize > 0, "batch-size-zero");
        require(bytes(_metadataURI).length > 0, "empty-metadata-uri");
        require(batches[_merkleRoot].merkleRoot == bytes32(0), "batch-already-exists");

        batches[_merkleRoot] = InvoiceBatch({
            merkleRoot: _merkleRoot,
            batchSize: _batchSize,
            issuer: msg.sender,
            metadataURI: _metadataURI,
            timestamp: block.timestamp
        });

        // Add to enumeration array
        batchRoots.push(_merkleRoot);

        // Update counters
        totalBatches++;
        totalInvoices += _batchSize;

        emit BatchAnchored(
            _merkleRoot,
            _batchSize,
            msg.sender,
            _metadataURI,
            block.timestamp,
            batchRoots.length - 1  // batchIndex
        );
    }

    // ============ VERIFICATION FUNCTIONS ============
    /// @notice Check if a batch is anchored
    /// @param _merkleRoot The merkle root to check
    /// @return true if batch exists
    function isBatchAnchored(bytes32 _merkleRoot) external view returns (bool) {
        return batches[_merkleRoot].merkleRoot != bytes32(0);
    }

    /// @notice Verify that an invoice hash belongs to a merkle tree
    /// @param _merkleRoot The merkle root of the batch
    /// @param _invoiceHash The hash of the invoice (typically hash of CID)
    /// @param _proof Array of merkle proof hashes
    /// @return true if invoice is valid member of batch
    function verifyInvoice(
        bytes32 _merkleRoot,
        bytes32 _invoiceHash,
        bytes32[] calldata _proof
    ) external returns (bool) {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        require(_invoiceHash != bytes32(0), "invalid-invoice-hash");

        bool isValid = MerkleProof.verify(_proof, _merkleRoot, _invoiceHash);
        require(isValid, "invalid-merkle-proof");

        emit InvoiceVerified(_merkleRoot, _invoiceHash, msg.sender, "");
        return true;
    }

    /// @notice Verify invoice CID directly with enhanced event emission
    /// @param _merkleRoot The merkle root of the batch
    /// @param _invoiceCID The IPFS CID of the invoice
    /// @param _proof Array of merkle proof hashes
    /// @return true if invoice CID is valid member of batch
    function verifyInvoiceByCID(
        bytes32 _merkleRoot,
        string calldata _invoiceCID,
        bytes32[] calldata _proof
    ) external returns (bool) {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        require(bytes(_invoiceCID).length > 0, "empty-invoice-cid");

        // Hash the CID string to get the leaf for merkle verification
        bytes32 cidHash = keccak256(abi.encodePacked(_invoiceCID));
        bool isValid = MerkleProof.verify(_proof, _merkleRoot, cidHash);
        require(isValid, "invalid-merkle-proof");

        emit InvoiceVerified(_merkleRoot, cidHash, msg.sender, _invoiceCID);
        return true;
    }

    /// @notice Verify invoice with raw leaf data (for flexible hashing strategies)
    /// @param _merkleRoot The merkle root of the batch
    /// @param _leaf The raw leaf data (could be CID, invoice data, or any content)
    /// @param _proof Array of merkle proof hashes
    /// @param _invoiceCID Optional invoice CID for event emission
    /// @return true if invoice is valid member of batch
    function verifyInvoiceWithLeaf(
        bytes32 _merkleRoot,
        bytes calldata _leaf,
        bytes32[] calldata _proof,
        string calldata _invoiceCID
    ) external returns (bool) {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        require(_leaf.length > 0, "empty-leaf");

        bytes32 leafHash = keccak256(abi.encodePacked(_leaf));
        bool isValid = MerkleProof.verify(_proof, _merkleRoot, leafHash);
        require(isValid, "invalid-merkle-proof");

        emit InvoiceVerified(_merkleRoot, leafHash, msg.sender, _invoiceCID);
        return true;
    }

    // ============ UTILITY FUNCTIONS FOR INDIVIDUAL INVOICE REGISTRATION ============
    /// @notice Register individual invoice CID mapping (for indexing purposes)
    /// @param _merkleRoot The batch merkle root
    /// @param _invoiceId Human-readable invoice ID
    /// @param _invoiceCID IPFS CID of the individual invoice
    /// @param _invoiceHash Hash used in merkle tree (should match CID hash or content hash)
    /// @dev This is optional, used for better off-chain indexing
    function registerIndividualInvoice(
        bytes32 _merkleRoot,
        string calldata _invoiceId,
        string calldata _invoiceCID,
        bytes32 _invoiceHash
    ) external onlyRole(SERVER_ROLE) {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        require(bytes(_invoiceId).length > 0, "empty-invoice-id");
        require(bytes(_invoiceCID).length > 0, "empty-invoice-cid");
        require(_invoiceHash != bytes32(0), "invalid-invoice-hash");

        emit IndividualInvoiceRegistered(_merkleRoot, _invoiceHash, _invoiceId, _invoiceCID);
    }

    // ============ QUERY FUNCTIONS ============
    /// @notice Get batch details by merkle root
    /// @param _merkleRoot The merkle root of the batch
    /// @return batch The InvoiceBatch struct with all details
    function getBatch(bytes32 _merkleRoot)
        external
        view
        returns (InvoiceBatch memory)
    {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        return batches[_merkleRoot];
    }

    /// @notice Get batch metadata URI
    /// @param _merkleRoot The merkle root of the batch
    /// @return metadataURI The IPFS URI containing batch metadata with individual CIDs
    function getMetadataURI(bytes32 _merkleRoot)
        external
        view
        returns (string memory)
    {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        return batches[_merkleRoot].metadataURI;
    }

    /// @notice Get issuer of a batch
    /// @param _merkleRoot The merkle root of the batch
    /// @return issuer Address of the batch issuer
    function getIssuer(bytes32 _merkleRoot)
        external
        view
        returns (address)
    {
        require(batches[_merkleRoot].merkleRoot != bytes32(0), "batch-not-found");
        return batches[_merkleRoot].issuer;
    }

    /// @notice Get merkle root by batch index (for enumeration)
    /// @param _batchIndex Index in the batchRoots array
    /// @return merkleRoot The merkle root at the given index
    function getBatchRootByIndex(uint256 _batchIndex)
        external
        view
        returns (bytes32)
    {
        require(_batchIndex < batchRoots.length, "batch-index-out-of-bounds");
        return batchRoots[_batchIndex];
    }

    /// @notice Get total number of batches for enumeration
    /// @return count Total number of batches
    function getBatchCount() external view returns (uint256) {
        return batchRoots.length;
    }

    /// @notice Get a range of batch roots for pagination
    /// @param _start Start index (inclusive)
    /// @param _end End index (exclusive)
    /// @return roots Array of merkle roots in the range
    function getBatchRoots(uint256 _start, uint256 _end)
        external
        view
        returns (bytes32[] memory)
    {
        require(_start < _end, "invalid-range");
        require(_end <= batchRoots.length, "end-out-of-bounds");

        bytes32[] memory result = new bytes32[](_end - _start);
        for (uint256 i = _start; i < _end; i++) {
            result[i - _start] = batchRoots[i];
        }
        return result;
    }

    // ============ ROLE MANAGEMENT ============
    /// @notice Grant SERVER_ROLE to a new address (admin only)
    /// @param _server Address to grant SERVER_ROLE
    function grantServerRole(address _server) external onlyRole(DEFAULT_ADMIN_ROLE) {
        require(_server != address(0), "invalid-server-address");
        _grantRole(SERVER_ROLE, _server);
    }

    /// @notice Revoke SERVER_ROLE from an address (admin only)
    /// @param _server Address to revoke SERVER_ROLE
    function revokeServerRole(address _server) external onlyRole(DEFAULT_ADMIN_ROLE) {
        _revokeRole(SERVER_ROLE, _server);
    }

    // ============ EMERGENCY CONTROLS ============
    /// @notice Pause all anchoring operations (admin only)
    /// @dev Use in case of emergency, bug discovery, or maintenance
    function pause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        _pause();
    }

    /// @notice Resume all anchoring operations (admin only)
    /// @dev Can only be called after pause()
    function unpause() external onlyRole(DEFAULT_ADMIN_ROLE) {
        _unpause();
    }

    // ============ OWNERSHIP MANAGEMENT ============
    /// @notice Transfer ownership to new admin (2-step process for safety)
    /// @param newOwner Address of the new owner (must accept ownership)
    /// @dev Step 1: Current owner initiates transfer
    function transferOwnership(address newOwner) public override onlyOwner {
        super.transferOwnership(newOwner);
    }

    /// @notice Accept ownership transfer (2-step process for safety)
    /// @dev Step 2: New owner must call this to complete transfer
    function acceptOwnership() public override {
        super.acceptOwnership();
    }
}