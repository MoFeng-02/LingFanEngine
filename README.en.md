# LingFan Engine

is a narrative and sandbox novel game engine built on .NET, C#, and Avalonia.

LingFan drives everything with the idea that "everything is List and Dict," while remaining AOT-friendly. Under AOT, it still keeps highly dynamic narrative capabilities — something I anticipated and have since verified.

The name "LingFan" didn't come out of nowhere; it was a flash of inspiration during the initial design. Yeah, that's how the name came about.

I drew inspiration from Ren'Py, but didn't copy it. I referenced some of its ideas and APIs, then made my own localization.

---

### What can LingFan Engine do?

- Build narrative novel games
- Build sandbox novel games
- Highly customizable extensibility (based on DI — you can replace most default behaviors)

---

### What is the recommended philosophy of LingFan Engine?

**Naturalness in world-building.**

Before you trigger an NPC or a scene, we recommend not writing it into the global state. We support scene-level NPC property injection — before you know them, they don't know you either. There is no data.

---

### What should we do?

The engine doesn't come with game-specific prefabs; it only prebuilds the core engine state. But we do provide some Handler capabilities — you can reuse them, or you can customize your own.

---

### Development Paradigm Recommendation

If you're a C# developer, I recommend you develop with AOT in mind.  
Because with AOT, security is better. AOT isn't hard — I recommend it as the primary development paradigm, but if you prefer, you can also develop in JIT mode.

### A word for DSL creators

If you're willing, the better approach is still to develop with C#. DSL is more suitable for writing stories.  
DSL is better for story flow because it natively supports text-level rollback. C# is better for extensions — if you need precise save/load, use DSL; if not, use C#.  
Yes, it's not hard. It's elegant. And you have plenty of options. 

## To the Community

First and foremost, thank you for stopping by. This engine is still young, but I believe it already carries something unique – a blend of AOT safety and dynamic storytelling, driven by nothing more than Lists and Dicts. If you find it useful, a star would mean the world to me; if you spot bugs or rough edges, please file an issue – I can't catch them all alone.

### Why I Built This

I love visual novels and sandbox games, but I couldn't find an engine that truly embraced AOT while staying flexible. So I built one from scratch – with AOT as the foundation, not an afterthought. The "List & Dict" philosophy turned out to strike a wonderful balance: you get both compile-time safety and runtime dynamism. I strongly encourage you to adopt the same AOT-first mindset when extending the engine – it's modern, secure, and future-proof.

For DSL authors: once you've compiled your game shell, you can write and iterate your entire story in DSL without recompiling. Encrypt your story files, and most data changes can be done in DSL alone – only new features require a recompile. This means you focus on storytelling, not rebuilding.
