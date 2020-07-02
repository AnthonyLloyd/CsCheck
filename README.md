# CsCheck

CsCheck is a C# random testing library inspired by QuickCheck.

It differs in that generation and shrinking are both based on [PCG](https://www.pcg-random.org), a fast random number generator.

This gives advantages particularly important for C#:

- Automatic shrinking. Gen classes are composable and there is no need for Arb classes. So less boilerplate.
- Random testing and shrinking can run in parallel. This and PCG make it very fast.
- Shrunk cases report the seed so they can be rerun. Any failure can easily be reproduced.
- Shrinking can be done offline and multiple times to give simpler cases for high dimensional generators.
