// SPDX-License-Identifier: MIT
pragma solidity ^0.8.0;

/**
 * @title InvoiceBatchContract
 * @dev Smart contract for storing invoice batch proofs on Ethereum blockchain
 */
contract InvoiceBatchContract {
    struct Batch {
        bytes32 merkleRoot;
        string batchId;
        string metadataCid;
        uint256 timestamp;
        address submitter;
        uint256 invoiceCount;
    }

    struct Invoice {
        bytes32 cidHash;
        uint256 invoiceId;
        uint256 batchIndex;
        uint256 timestamp;
        address submitter;
    }

    // Storage
    mapping(string => Batch) public batches;
    mapping(uint256 => Invoice) public invoices;
    mapping(bytes32 => bool) public merkleRoots;
    mapping(bytes32 => bool) public cidHashes;

    // Arrays for enumeration
    string[] public batchIds;
    uint256[] public invoiceIds;

    // Events
    event BatchSubmitted(
        string indexed batchId,
        bytes32 indexed merkleRoot,
        string metadataCid,
        uint256 timestamp,
        address submitter,
        uint256 invoiceCount
    );

    event InvoiceSubmitted(
        uint256 indexed invoiceId,
        bytes32 indexed cidHash,
        uint256 batchIndex,
        uint256 timestamp,
        address submitter
    );

    // Modifiers
    modifier batchNotExists(string memory batchId) {
        require(batches[batchId].timestamp == 0, "Batch already exists");
        _;
    }

    modifier invoiceNotExists(uint256 invoiceId) {
        require(invoices[invoiceId].timestamp == 0, "Invoice already exists");
        _;
    }

    modifier validMerkleRoot(bytes32 merkleRoot) {
        require(merkleRoot != bytes32(0), "Invalid merkle root");
        require(!merkleRoots[merkleRoot], "Merkle root already used");
        _;
    }

    modifier validCidHash(bytes32 cidHash) {
        require(cidHash != bytes32(0), "Invalid CID hash");
        _;
    }

    /**
     * @dev Submit a batch of invoices with Merkle root
     * @param merkleRoot The Merkle root of the batch
     * @param batchId Unique identifier for the batch
     * @param metadataCid IPFS CID containing batch metadata
     */
    function submitBatch(
        bytes32 merkleRoot,
        string memory batchId,
        string memory metadataCid
    ) 
        external 
        batchNotExists(batchId)
        validMerkleRoot(merkleRoot)
    {
        // Create batch record
        batches[batchId] = Batch({
            merkleRoot: merkleRoot,
            batchId: batchId,
            metadataCid: metadataCid,
            timestamp: block.timestamp,
            submitter: msg.sender,
            invoiceCount: 0
        });

        // Mark merkle root as used
        merkleRoots[merkleRoot] = true;
        
        // Add to enumeration
        batchIds.push(batchId);

        emit BatchSubmitted(
            batchId,
            merkleRoot,
            metadataCid,
            block.timestamp,
            msg.sender,
            0
        );
    }

    /**
     * @dev Submit an individual invoice
     * @param cidHash Hash of the invoice CID
     * @param invoiceId Unique identifier for the invoice
     */
    function submitInvoice(
        bytes32 cidHash,
        uint256 invoiceId
    )
        external
        invoiceNotExists(invoiceId)
        validCidHash(cidHash)
    {
        // Create invoice record
        invoices[invoiceId] = Invoice({
            cidHash: cidHash,
            invoiceId: invoiceId,
            batchIndex: 0, // Individual submission
            timestamp: block.timestamp,
            submitter: msg.sender
        });

        // Mark CID hash as used
        cidHashes[cidHash] = true;

        // Add to enumeration
        invoiceIds.push(invoiceId);

        emit InvoiceSubmitted(
            invoiceId,
            cidHash,
            0,
            block.timestamp,
            msg.sender
        );
    }

    /**
     * @dev Update batch with invoice count (called after all invoices in batch are processed)
     * @param batchId The batch identifier
     * @param invoiceCount Number of invoices in the batch
     */
    function updateBatchInvoiceCount(
        string memory batchId,
        uint256 invoiceCount
    ) external {
        require(batches[batchId].timestamp > 0, "Batch does not exist");
        require(batches[batchId].submitter == msg.sender, "Not batch submitter");
        
        batches[batchId].invoiceCount = invoiceCount;
    }

    /**
     * @dev Verify if a CID is part of a batch using Merkle proof
     * @param batchId The batch identifier
     * @param cidHash The CID hash to verify
     * @param proof The Merkle proof
     * @return bool True if the proof is valid
     */
    function verifyInvoiceInBatch(
        string memory batchId,
        bytes32 cidHash,
        bytes32[] memory proof
    ) external view returns (bool) {
        Batch memory batch = batches[batchId];
        require(batch.timestamp > 0, "Batch does not exist");

        return verifyMerkleProof(proof, cidHash, batch.merkleRoot);
    }

    /**
     * @dev Verify Merkle proof
     * @param proof Array of sibling hashes
     * @param leaf The leaf hash to verify
     * @param root The Merkle root
     * @return bool True if proof is valid
     */
    function verifyMerkleProof(
        bytes32[] memory proof,
        bytes32 leaf,
        bytes32 root
    ) public pure returns (bool) {
        bytes32 computedHash = leaf;

        for (uint256 i = 0; i < proof.length; i++) {
            bytes32 proofElement = proof[i];
            if (computedHash <= proofElement) {
                computedHash = keccak256(abi.encodePacked(computedHash, proofElement));
            } else {
                computedHash = keccak256(abi.encodePacked(proofElement, computedHash));
            }
        }

        return computedHash == root;
    }

    /**
     * @dev Get batch information
     * @param batchId The batch identifier
     * @return Batch struct
     */
    function getBatch(string memory batchId) 
        external 
        view 
        returns (Batch memory) 
    {
        return batches[batchId];
    }

    /**
     * @dev Get invoice information
     * @param invoiceId The invoice identifier
     * @return Invoice struct
     */
    function getInvoice(uint256 invoiceId) 
        external 
        view 
        returns (Invoice memory) 
    {
        return invoices[invoiceId];
    }

    /**
     * @dev Get total number of batches
     * @return uint256 Number of batches
     */
    function getBatchCount() external view returns (uint256) {
        return batchIds.length;
    }

    /**
     * @dev Get total number of individual invoices
     * @return uint256 Number of invoices
     */
    function getInvoiceCount() external view returns (uint256) {
        return invoiceIds.length;
    }

    /**
     * @dev Get batch ID by index
     * @param index The index
     * @return string Batch ID
     */
    function getBatchIdByIndex(uint256 index) 
        external 
        view 
        returns (string memory) 
    {
        require(index < batchIds.length, "Index out of bounds");
        return batchIds[index];
    }

    /**
     * @dev Get invoice ID by index
     * @param index The index
     * @return uint256 Invoice ID
     */
    function getInvoiceIdByIndex(uint256 index) 
        external 
        view 
        returns (uint256) 
    {
        require(index < invoiceIds.length, "Index out of bounds");
        return invoiceIds[index];
    }

    /**
     * @dev Check if a merkle root has been used
     * @param merkleRoot The merkle root to check
     * @return bool True if used
     */
    function isMerkleRootUsed(bytes32 merkleRoot) 
        external 
        view 
        returns (bool) 
    {
        return merkleRoots[merkleRoot];
    }

    /**
     * @dev Check if a CID hash has been used
     * @param cidHash The CID hash to check
     * @return bool True if used
     */
    function isCidHashUsed(bytes32 cidHash) 
        external 
        view 
        returns (bool) 
    {
        return cidHashes[cidHash];
    }
}