/**
 * ModelPage — pure 3D viewer for the ThatOpen Fragments render artifact
 * produced by vos.Tools.IfcIngest.
 *
 * This is the scaffold (Feature #5248 sub-task A). Loader, three.js scene,
 * picking, navigation, and loading UX land in subsequent sub-tasks (B–F).
 */
export function ModelPage() {
  return (
    <div className="flex-1 flex flex-col p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-white">Model</h1>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          3D viewer for the IFC-derived Fragments artifact.
        </p>
      </header>
      <div
        role="region"
        aria-label="Fragments viewer placeholder"
        data-testid="model-viewer-placeholder"
        className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex flex-col items-center justify-center text-center p-8"
      >
        <p className="text-lg font-medium text-zinc-700 dark:text-zinc-300">
          3D viewer lands here.
        </p>
        <p className="text-sm text-zinc-500 dark:text-zinc-500 mt-2 max-w-md">
          The ThatOpen Fragments scene, navigation controls, and pick-to-metadata
          wiring arrive in follow-up sub-tasks of Feature&nbsp;#5248.
        </p>
      </div>
    </div>
  );
}
