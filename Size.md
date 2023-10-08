# Size algorithm

Size restricted to not overflow.

## product type that have a fixed number of elements e.g. Select, Tuples
size = sum element size
pass the min to the element gen
bail out if partial size larger than min

## sum type e.g. Enum, OneOf - DONE
l = number of choices
if (min is not null && min.I < l) l = (uint)min.I + 1;
i = Next(l)
if i = min.I pass min.Next to choice gen - smaller sum type should have no restriction on values
size = Size(i of choice, size of choice)

## collections that have a variable number of elements e.g. Array, Array2D
minN = ShiftDown(min.I)
n = Next(Math.Min(max length + 1, minN + 1))
if n = minN pass min.Next to element gens else null
size = Size(ShiftUp(n), sum element size)
bail out if partial size larger than min

ShiftUp and ShiftDown functions because when collections are in a product type we really want collection lengths to be shrunk first.
For fixed length collections size = sum element size. Do we need to ShiftUp the size? What are the examples of generating fixed length?

## int, long
v = start + Next(Math.Min(finish - start + 1, UnZigZag(min.I) - start + 1))
size = Size(ZigZag(v))

## uint, ulong
v = start + Next(Math.Min(finish - start + 1, min.I - start + 1))
size = Size(v)

## double, float
