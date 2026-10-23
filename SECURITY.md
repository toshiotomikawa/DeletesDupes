# Safety and privacy

DeletesDupes is a local, path-based Windows file utility. Back up important data
before use. Do not run it on a changing, untrusted or adversarial folder tree.
It is not a sandbox, transactional deduplicator or backup system.

The pre-delete callback rejects missing recycle intent and failed validation.
The TSF flag means "recycle if possible", not a guarantee of recovery for every
Shell provider or policy. A post-delete result detects a missing destination
afterwards and cannot restore a file the Shell already removed. Fixed-drive and
path exclusions, protected survivors and revalidation are additional safeguards.
The final validation-to-Shell interval remains a concurrency limitation.

The application reads selected image candidates and writes local logs containing
full paths, filenames, hashes and diagnostic errors. It makes no network requests.
Cloud synchronization software can still propagate filesystem changes elsewhere.
Never attach private images or unredacted logs to public issues. Describe defects
with small synthetic files and redacted paths. Use an available private repository
security-reporting channel for sensitive reports; do not assume one is enabled.

Tests simulate recycling and preserve their fixtures. No production installer,
registry change or real Shell recycle/restore cycle is part of the test suite.
Actual recovery on a disposable Windows folder is a separate manual verification
requiring deliberate operator approval; it has not been certified by these tests.

Microsoft API references:
- [PreDeleteItem cancellation](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperationprogresssink-predeleteitem)
- [TRANSFER_SOURCE_FLAGS](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/ne-shobjidl_core-_transfer_source_flags)
- [SetOperationFlags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
