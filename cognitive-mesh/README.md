# Cognitive Mesh Workspace

> All Cognitive Mesh artifacts now live under this directory. Use it as the single source of truth for docs, DSL libraries, Aspire hosts, and future runtime services.

## Structure

```text
cognitive-mesh/
├── docs/                         # Product vision, PRD, architecture, DSL spec
├── CognitiveMesh.App/            # Actor-runtime aware worker (hosts Aevatar Agents)
├── CognitiveMesh.AppHost/        # Aspire host orchestrating Orleans + observability
├── dsl/
│   ├── Aevatar.CognitiveMesh.Dsl/       # DSL compiler library
│   └── Aevatar.CognitiveMesh.Dsl.Tests/ # DSL compiler tests
└── README.md
```

## Next Steps
- Flesh out **CognitiveMesh.App** to translate validated DSL blueprints into Orleans grains + Aevatar Agents.
- Extend **CognitiveMesh.AppHost** with actual Orleans silos, dashboards, and storage resources via Aspire.
- Keep **docs/** updated whenever architecture or hosting contracts evolve; treat it as the canonical PRD.
