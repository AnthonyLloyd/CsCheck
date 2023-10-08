# Size algorithm

Size restricted to not overflow.
min can only be used to bail out. It can't be used to restrict the Next() since the min won't be available when reproducing.

## product type that have a fixed number of elements e.g. Select, Tuples - DONE
size = sum element size
pass the min to the element gen
bail out if partial size larger than min

## sum type e.g. Enum, OneOf - DONE
l = number of choices
i = Next(l)
if i = min.I pass min.Next to choice gen - smaller sum type should have no restriction on values
size = Size(i of choice, size of choice)

## collections that have a variable number of elements e.g. Array, Array2D
n = Next(max length + 1)
if n = ShiftDown(min.I) pass min.Next to element gens else null
size = Size(ShiftUp(n), sum element size)
bail out if partial size larger than min

ShiftUp and ShiftDown functions because when collections are in a product type we really want collection lengths to be shrunk first.
For fixed length collections size = sum element size. Do we need to ShiftUp the size? What are the examples of generating fixed length?

## int, long
v = start + Next(finish - start + 1)
size = Size(ZigZag(v))

## uint, ulong
v = start + Next(finish - start + 1)
size = Size(v)

## double, float
