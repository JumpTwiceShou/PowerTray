# Local patches

No local source patches are required. The build consumes the immutable official
archive recorded in `../source.json`. The individual upstream commits that make
up the required patch range are pinned in `../required-upstream-commits.txt`.

If a future build needs a local change, store it in this directory as a reviewable
patch and add its file name and SHA-256 to `source.json`. The build must fail if a
declared patch is missing or has a different hash.
