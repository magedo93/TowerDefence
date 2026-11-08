
# Unity Tower Defence - AI Development Instructions

## Project Information
- Engine: Unity 6
- Language: C#
- Platform: Windows
- Game Type: 2D Tower Defence
- Original Engine: Unity 2018

## Primary Objectives
1. Modernize the legacy project for Unity 6.
2. Preserve all existing gameplay functionality.
3. Improve code quality and maintainability.
4. Support adding new towers, enemies, levels, and features.
5. Improve performance and stability.

## Rules
- Analyze existing code before modifying it.
- Preserve Unity asset GUIDs and .meta files.
- Never delete scenes, prefabs, or assets without approval.
- Avoid unnecessary third-party packages.
- Use Unity 6-compatible APIs.
- Do not edit Library/, Temp/, or generated files.
- Keep changes small and reviewable.
- Explain all modifications.

## Development Workflow
1. Analyze requirements.
2. Inspect affected scripts and assets.
3. Propose implementation.
4. Wait for approval.
5. Implement changes.
6. Validate compilation where tooling permits.
7. Provide Unity Editor testing instructions.

## Architecture Guidelines
- Prefer modular and maintainable components.
- Use ScriptableObjects for suitable game configuration.
- Avoid unnecessary singleton dependencies.
- Preserve existing object pooling behavior.
- Keep gameplay logic independent of UI where practical.
- Avoid large rewrites without approval.

## Testing
- Do not claim successful Unity compilation unless verified.
- Report missing references and Inspector changes.
- Check scene and prefab compatibility.
- Provide manual gameplay test steps.
