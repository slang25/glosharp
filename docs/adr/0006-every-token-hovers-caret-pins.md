# Every symbol-bearing token gets a hover; `^?` means "pinned", not "hover here"

Unlike twoslash, where `^?` is the only way to get type info, GloSharp emits a hover for every token in the visible code that resolves to a symbol, and `^?` marks a hover as persistent (always shown). Readers get IDE-like hovers without authors annotating anything; the cost is larger JSON and HTML, which we accept. A token only gets a hover if it carries its own symbol (identifiers, predefined types, `this`/`base`, `new`); keywords and operators never borrow the enclosing method's or declaration's hover.
