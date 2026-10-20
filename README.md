# Aurum

A five-reel console slot machine with 15 paylines, animated spins, and an integrated anti-cheat panel. All wagers use play credits.

## Gameplay

Each spin selects a stop on five virtual reel strips. Base play and free spins use separate fixed strips; free spins have fewer new wilds because wilds persist across the feature. Three consecutive positions on each circular strip form the visible 5 x 3 board. All 15 fixed paylines are evaluated from left to right, and each line pays only its highest-value valid combination.

Paid spins reserve the selected bet before drawing an outcome. The result is fixed before animation and settled once afterward. A random-source failure aborts the draw and refunds the wager; an interrupted feature draw leaves the feature unchanged for retry.

### Paytable

The **line bet is the total selected bet divided by 15**. Multipliers below apply to that line bet.

| Symbol | 3 matches | 4 matches | 5 matches |
| :--- | ---: | ---: | ---: |
| `3`, `4`, `5` | 6x | 15x | 45x |
| `A`, `K` | 15x | 36x | 120x |
| `C` - crown | 30x | 90x | 300x |
| `W` - wilds only | 45x | 150x | 450x |

`W` substitutes for regular symbols. The evaluator compares every eligible substitution and the wild-only prefix, then awards the best result on that line. Wilds do not add a separate doubling multiplier. `S` and `O` break a line's matching sequence.

For a **$3 total bet**, the line bet is **$0.20**. Three matching `A` symbols pay **$3.00**. A line starting `W W W A K` pays **$9.00** for the three wilds, which is higher than the four-symbol `A` payout of $7.20. It is not paid twice.

Line wins are summed and rounded to cents. A losing paid spin still costs its wager.

### Sticky-wild free spins

Scatters are counted by reel: multiple `S` symbols on one reel count once.

| Scatter-bearing reels | Free spins |
| :--- | ---: |
| 3 | 8 |
| 4 | 12 |
| 5 | 20 |

The feature locks its triggering bet and starts at **2x line payouts**. Wilds landing during the feature remain in their positions, marked by cyan borders. A spin containing one or more new sticky wilds raises the multiplier by one for the **next** free spin, up to **5x**. Existing sticky wilds do not raise it again.

Three or more scatter-bearing reels retrigger five spins. A maximum of **50 free spins can be awarded across one feature**, including its initial award. Sticky positions and the multiplier reset when the feature finishes. Free spins do not charge another wager.

The bonus buy awards **15 free spins for 50x the selected bet**. At a $3 bet, the purchase costs $150. Its cost is shown below the reels.

### Coin respins

Six or more `O` symbols trigger a coin feature with **three respins**. Initial coins lock in place; only empty cells are redrawn.

| Coin value | Weight |
| :--- | ---: |
| 1x the locked total bet | 70% |
| 2x | 20% |
| 3x | 7% |
| 5x | 2% |
| 10x | 0.8% |
| 25x | 0.2% |

Each empty cell has a **12% chance** of receiving a coin on a respin. New coins retain their values and reset the counter to three. Three consecutive misses or a full board ends the feature. All coin values are collected **once**, and free-spin multipliers do not apply to them.

Line wins settle first, then coin respins, then any queued free spins. A coin feature triggered during free spins pauses that feature and resumes it afterward without consuming an extra free spin.

### Randomness

Gameplay draws use the operating system's cryptographic random source through `RandomNumberGenerator`. Rejection sampling maps random bytes to bounded values without modulo bias. Animation uses a separate generator and cannot change the outcome stream.

The reel strips, paytable, and bonus weights are fixed game data. Outcome probabilities do not adapt to the player's balance, prior wins, or animation timing. Automated tests use an injected deterministic source; gameplay never falls back to it if the cryptographic source fails.

The RTP target is **98.9%**, measured separately for regular play (including triggered features) and 50x bonus purchases. RTP describes the long-run return over total wagers, not the result of a session. Fixed reel weights balance the larger paytable and sticky-wild feature; outcomes never adjust to make up previous losses. Simulation results are estimates, not a certified or proven theoretical RTP.

## Controls

| Key | Action |
| :--- | :--- |
| **Space** | Spin, or retry a paused feature |
| **Up / Down** | Adjust bet outside a feature |
| **C** | Add $500 |
| **F** | Buy 15 free spins |
| **Tab** | Toggle the anti-cheat panel |
| **PgUp / PgDn** | Browse processes or switch help pages |
| **H** | Show the paytable and bonus rules |
| **Q / Esc** | Return to the menu; queued features resume on re-entry |

## Anti-cheat

The anti-cheat combines process-handle monitoring with balance integrity checks. Its status and process list are rendered inside the game UI.

### Handle monitoring and trust validation

On each scan, `NtQuerySystemInformation` retrieves an extended system-handle snapshot. The scanner identifies the game's process object and matches external handles against that object, using full-width process IDs and handle values.

Each matching handle is associated with its owning process. The scanner combines that process's access flags and counts its handles. Trust is evaluated once per owner within each scan:

- **Recognized system processes:** The executable name must be on the allowlist, and its directory must match the Windows system directory.
- **Visual Studio tools:** The supported debug console and data collectors must pass `WinVerifyTrust`, retain their expected original filename, and have a signing certificate identifying Microsoft Corporation.
- **Other applications:** Readable process identities that fail those checks are listed as other apps.
- **Unverified processes:** If the executable path cannot be queried, the owner is listed as not verified rather than assumed trusted.

Recognized processes remain visible in the process list. The game's own handles and reserved system PIDs are excluded. Administrator privileges improve visibility into other processes.

**An attachment or read/write permission alone never triggers punishment.** Handle flags describe granted access; they do not establish that an RPM or WPM call occurred.

### Balance storage and protection

The balance is stored in a dedicated **4 KB page** allocated with `VirtualAlloc`. The page normally has `PAGE_NOACCESS` protection. Legitimate balance operations briefly switch it to `PAGE_READWRITE`, access the stored values, and restore `PAGE_NOACCESS`.

Each legitimate write generates a new 64-bit key. The balance's IEEE 754 bit pattern and a consistency guard are stored as:

```text
encoded balance = balance bits XOR key
encoded guard   = (balance bits + key) XOR key
```

The key is stored in the same protected page. The guard calculation uses 64-bit wraparound arithmetic. Wallet operations and integrity checks share a lock so legitimate game updates do not race with the scanner.

### Tamper detection and punishment

Whenever the game reads the balance, it decodes both values and verifies:

```text
decoded balance bits + key == decoded guard
```

The background scanner also performs this check on each cycle, with a 250 ms pause between scans. A failed check indicates that the stored values no longer form a valid balance record.

The game then resets the balance to **$0**, writes a fresh key and guard, restores page protection, and records the event in the anti-cheat panel. This response is independent of the handle owner's trust classification: even a recognized process does not exempt a corrupted balance from validation.

The mechanism detects inconsistent edits; it cannot attribute the write to a particular process. XOR encoding is obfuscation, not cryptographic encryption. Page protection blocks ordinary access while active, but it is not a security boundary against a sufficiently privileged process. Coordinated changes that preserve the balance, key, and guard relationship can evade the current check.
