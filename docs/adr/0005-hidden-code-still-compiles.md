# Regions and cut markers only hide lines; the whole file still compiles

`--region` and the cut markers are display concerns: the full source is compiled and lines outside the region or above the cut are masked out of the rendered code. Usings, helper types and setup outside the visible part therefore still drive types, hovers and diagnostics. Compiling only the visible part would be cheaper but produce wrong types and false errors.
