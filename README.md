# CsCheck

![CI](https://github.com/AnthonyLloyd/CsCheck/workflows/CI/badge.svg>branch=master)[![NuGet](https://buildstats.info/nuget/CsCheck?includePreReleases=true)](https://www.nuget.org/packages/CsCheck/)

CsCheck is a C# random testing library inspired by QuickCheck.

It differs in that generation and shrinking are both based on [PCG](https://www.pcg-random.org), a fast random number generator.

This gives advantages particularly important for C#:

- Automatic shrinking. Gen classes are composable with no need for Arb classes. So less boilerplate.
- Random testing and shrinking can run in parallel. This and PCG make it very fast.
- Shrunk cases report the seed so they can be rerun. Any failure can easily be reproduced.
- Shrinking can be done multiple times to give simpler cases for high dimensional generators.
