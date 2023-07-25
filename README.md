# Chess Coding Challenge (C#) Example

This is an example bot for Seb Lague's [Chess Coding Challenge](https://youtu.be/iScy18pVR58),
it implements only the most basic features for a functional chess engine. Little effort has
been made to optimise for tokens, apart from implementing Quiescence Search inside the normal
search function (rather than in a separate function).

### Search
- Alpha-Beta Negamax
- Quiescence Search
- Iterative Deepening
- Transposition Table (Ordering & Cutoffs)
- MVV-LVA for Captures

### Evaluation
- Quantised & Compressed PeSTO Piece-Square Tables
