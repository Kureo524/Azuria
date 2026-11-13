// Zone utile du tiroir. Le verre (flou + voile, congés concaves compris) est composé côté natif
// (GlassSurface.cs) ; la page reste transparente et se cale dans le corps du tiroir, hors congés.
// Paramètres d'URL fournis par le C# : l, t, r, b = marges en px CSS.
(() => {
  const q = new URLSearchParams(location.search);
  const root = document.documentElement.style;
  for (const [k, v] of [['l', 'left'], ['t', 'top'], ['r', 'right'], ['b', 'bottom']])
    root.setProperty('--inset-' + k, parseFloat(q.get(k) || '0') + 'px');
})();
