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
}
