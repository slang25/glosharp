# Popups use CSS where it's good enough and JavaScript where it isn't; "no runtime JS" is not a rule

The original brief was "no runtime JS": popups positioned purely with CSS anchor positioning. In practice browser support for anchored popovers is still not good enough, so we dropped the rule as too dogmatic. The Shiki stylesheet and the HTML renderer still position popups with CSS anchor positioning (with an `@supports not` fallback for browsers without it), while the Expressive Code plugin ships a small client module that moves popups out of the scrolling `<pre>`, flips and clamps them to the viewport, follows horizontal scroll and adds keyboard navigation.

## Consequences

- The HTML renderer's output stays script-free, but because of where it's embedded, not on principle: GitBook's frame strips `<script>` from fetched fragments, and published fragments are untrusted HTML on someone else's page.
- Don't reject a JS-based popup improvement in an integration just because it adds runtime JS.
