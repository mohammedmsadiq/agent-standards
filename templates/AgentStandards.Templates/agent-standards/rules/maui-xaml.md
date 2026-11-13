---
description: XAML conventions for .NET MAUI pages, views and resources.
globs: ["**/*.xaml"]
---
# MAUI XAML

- Set `x:DataType` for compiled bindings on pages, views and data templates.
- Prefer `Grid` and shallow layouts; use `CollectionView` for lists.
- Use styles and resources from the app's resource dictionaries instead of hard-coded colours and sizes.
- Add `SemanticProperties.Description`/`Hint`/`HeadingLevel` for accessibility.
- Keep logic out of code-behind unless it is purely visual.
