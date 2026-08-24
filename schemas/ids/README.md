# IDS 1.0.0

`ids.xsd` is the published buildingSMART schema, verbatim, from
`buildingSMART/IDS` at `Schema/ids.xsd`. It is what `IdsWriter` writes and what
`IdsReader` reads, and it is here so the shape a change has to satisfy is in the
repository rather than in somebody's memory.

**It is not wired into CI, and that is not an oversight.** The schema opens with
three imports:

```xml
<xs:import namespace="http://www.w3.org/XML/1998/namespace"    schemaLocation="http://www.w3.org/2001/xml.xsd"/>
<xs:import namespace="http://www.w3.org/2001/XMLSchema"        schemaLocation="http://www.w3.org/2001/XMLSchema.xsd"/>
<xs:import namespace="http://www.w3.org/2001/XMLSchema-instance" schemaLocation="http://www.w3.org/2001/XMLSchema-instance"/>
```

`xmllint --schema` resolves those over the network. Only the second one matters:
`idsValue` is `<xs:element ref="xs:restriction"/>` and `applicability` carries
`<xs:attributeGroup ref="xs:occurs"/>`, and both live in `XMLSchema.xsd` — 90KB
of machinery for two declarations. The W3C host was unreachable from the
environment this was written in, so `xsd-subset.xsd` declares exactly those two
instead, and `validate.sh` redirects the import to it. The check then runs with
no network at all.

That makes the restriction bodies laxer than the real schema would: a
`<xs:pattern>` is checked for having a `value` attribute and nothing more. What
IDS itself constrains is checked in full, and it is where the mistakes were:

| Rule | Caught |
|---|---|
| `applicability` is a sequence: entity, partOf, classification, attribute, property, material | yes |
| at most one `entity` in `applicability` | yes |
| `classification` holds `value` before `system` | yes |
| an `entity` requirement has no cardinality | yes |
| `partOf` cardinality is required or prohibited, never optional | yes |
| `relation` is one of five | yes |
| `ifcVersion` is IFC2X3, IFC4 or IFC4X3_ADD2 | yes |
| `author` is an email address | yes |
| `dataType` is all capitals | yes |

Every one of those is a mistake the first draft of `IdsWriter` made.
