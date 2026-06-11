namespace Schemata.Abstractions.Entities;

/// <summary>
///     Standard CRUD operations for resource APIs, including
///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>,
///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>,
///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>,
///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>,
///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
/// </summary>
public enum Operations
{
    /// <summary>
    ///     List resources matching a filter, per
    ///     <seealso href="https://google.aip.dev/132">AIP-132: Standard methods: List</seealso>.
    /// </summary>
    List,

    /// <summary>
    ///     Retrieve a single resource by name, per
    ///     <seealso href="https://google.aip.dev/131">AIP-131: Standard methods: Get</seealso>.
    /// </summary>
    Get,

    /// <summary>
    ///     Create a new resource, per
    ///     <seealso href="https://google.aip.dev/133">AIP-133: Standard methods: Create</seealso>.
    /// </summary>
    Create,

    /// <summary>
    ///     Update an existing resource, per
    ///     <seealso href="https://google.aip.dev/134">AIP-134: Standard methods: Update</seealso>.
    /// </summary>
    Update,

    /// <summary>
    ///     Delete a resource, per
    ///     <seealso href="https://google.aip.dev/135">AIP-135: Standard methods: Delete</seealso>.
    /// </summary>
    Delete,

    /// <summary>
    ///     Restore a soft-deleted resource, per
    ///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    Undelete,

    /// <summary>
    ///     Permanently remove a soft-deleted resource, per
    ///     <seealso href="https://google.aip.dev/164">AIP-164: Soft delete</seealso>.
    /// </summary>
    Expunge,

    /// <summary>
    ///     Purge resources matching a filter through a long-running operation, per
    ///     <seealso href="https://google.aip.dev/165">AIP-165: Purge</seealso>.
    /// </summary>
    Purge,
}
