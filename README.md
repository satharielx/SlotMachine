# Slot Machine Game
Slot machine game written in C# with a console interface by Sathariel.
# Rules

You get a winning combination in this slot by matching symbols to form a line from the reels on the machine. This slot machine game requires you to match at least three consecutive symbols to get a winning combination, but some demand you get five or more.  

To get an idea of how you can win when gambling at this game, you can see some examples below:

# Winning Combinations (depends on symbol)  

💜💜💜💔💔 – Three identical symbols next to each other in a line

💜💜💜💜💔 – Four identical symbols next to each other in a line

💜💜💜💜💜 – Five identical symbols next to each other in a line

If you get Five identical symbols on all of the reels this is called Jackpot. When this occurs your win is incresed by (10^4)x your bet.

# Losing Combinations

💜💜♠💔💔 – Two symbols in a line (you will need a minimum of three)

# Payouts

Payouts are based on the matched symbol, it's multiplier and its count on a reel.

# Winning Table

A - (3 matched on a single line) = 10x your bet.

A - (4 matched on a single line) = 10x your bet * times matched.

A - (5 matched on a single line) = 10x your bet * times matched * 10;


K - (3 matched on a single line) = 3x your bet.

K - (4 matched on a single line) = 3x your bet * times matched.

K - (5 matched on a single line) = 3x your bet * times matched * 10;


♥ - (3 matched on a single line) = 2.5x your bet.

♥ - (4 matched on a single line) = 2.5x your bet * times matched.

♥ - (5 matched on a single line) = 2.5x your bet * times matched * 10;


♦ - (3 matched on a single line) = 1.3x your bet.

♦ - (4 matched on a single line) = 1.3x your bet * times matched.

♦ - (5 matched on a single line) = 1.3x your bet * times matched * 10;


♣ - (3 matched on a single line) = 0.7x your bet.

♣ - (4 matched on a single line) = 0.7x your bet * times matched.

♣ - (5 matched on a single line) = 0.7x your bet * times matched * 10;

# Commands

There are several commands that you can use to interact with the game.


addBalance - Adds additional $20 to player's account.

generateSlot [betAmount] - Generates slot reels based on a bet amount.
