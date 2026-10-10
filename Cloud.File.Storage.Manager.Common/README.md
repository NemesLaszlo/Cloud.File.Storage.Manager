# Cloud File Storage Manager

It is the common basis of file manager nuget packages that provides definitions for various file operations, on which any package can be built.
Provides complete implementation to handle easily cloud file storage operations like get informations about files, reading, downloadURL generation, file update, file move and delete.

### Path rules

Every path is relative to the root directory of the storage. Paths can be given as a single string or as segments, and every segment is split on `/` and `\` and checked before any storage call:

- Empty or whitespace segments, segments made only of dots and spaces (such as `..`) and segments with control characters are rejected with an `ArgumentException`.
- The same checks run on the percent-decoded forms of a segment, so encoded traversal such as `%2e%2e` is rejected too. A segment whose decoded form contains `/` or `\` is rejected.
- A storage package can add its own segment rules on top of these.

### Root protection

The root directory itself cannot be changed: deleting it, updating it, moving it or opening a writeable local stream on it throws an `InvalidOperationException`. Reading it as a file or asking a download URL for it throws an `ArgumentException`. Asking file information for the root returns a non-existing entry.

### Other behaviour

- Moving a file to its own path does nothing.
- File information objects returned by the storage can be passed back to the read, download URL, update, move and delete methods; they always resolve under the root directory.
- `SupportsDownloadUrl` tells whether the storage can generate download URLs. When it is false (Physical), the download URL methods throw a `NotSupportedException`.
- A writeable local stream is uploaded when it is disposed, also after an exception, so partially written content is uploaded. A stream can only be committed by the storage that created it, and only to a path under its root.
- `ToJson` serializes the storage options, including keys, passwords and tokens, so its output must never be logged.
