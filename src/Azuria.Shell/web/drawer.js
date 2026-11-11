// Dessine la forme du tiroir — identique à la découpe native de la fenêtre (Native/Shaping.cs) —
// et expose la zone utile (hors congés concaves) en variables CSS --inset-*.
// Paramètres d'URL fournis par le C# : edge = top | bottom | left | right, r = rayon (px CSS).
(() => {
  const q = new URLSearchParams(location.search);
  const edge = q.get('edge') || 'top';
  const r = parseFloat(q.get('r') || '14');

  // Marges de la zone utile : les congés occupent r px sur les côtés concernés.
  const inset = { top: [r, r], bottom: [r, r], left: [0, r], right: [r, 0] }[edge] || [0, 0];
  const root = document.documentElement.style;
  root.setProperty('--inset-l', inset[0] + 'px');
  root.setProperty('--inset-r', inset[1] + 'px');

  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('class', 'drawer');
  svg.setAttribute('aria-hidden', 'true');
  const fill = document.createElementNS(NS, 'path');
  fill.setAttribute('class', 'drawer-fill');
  const line = document.createElementNS(NS, 'path');
  line.setAttribute('class', 'drawer-edge');
  svg.append(fill, line);
  document.body.prepend(svg);

  const arc = (R, sweep, x, y) => `A ${R} ${R} 0 0 ${sweep} ${x} ${y}`;

  // Renvoie [contour plein, trait des bords libres (le bord collé à l'écran n'est pas tracé)].
  function shape(w, h) {
    const R = Math.min(r, w / 2, h / 2);
    let body;
    switch (edge) {
      case 'bottom':
        body = `${arc(R, 1, w - R, h - R)} V ${R} ${arc(R, 0, w - 2 * R, 0)} H ${2 * R} ` +
               `${arc(R, 0, R, R)} V ${h - R} ${arc(R, 1, 0, h)}`;
        return [`M 0 ${h} H ${w} ${body} Z`, `M ${w} ${h} ${body}`];
      case 'left':
        body = `${arc(R, 0, w - R, R)} V ${h - R} ${arc(R, 0, w, h)}`;
        return [`M 0 0 H ${w} ${body} H 0 Z`, `M ${w} 0 ${body}`];
      case 'right':
        body = `${arc(R, 1, R, R)} V ${h - R} ${arc(R, 1, 0, h)}`;
        return [`M ${w} 0 H 0 ${body} H ${w} Z`, `M 0 0 ${body}`];
      default: // top
        body = `${arc(R, 0, w - R, R)} V ${h - R} ${arc(R, 1, w - 2 * R, h)} H ${2 * R} ` +
               `${arc(R, 1, R, h - R)} V ${R} ${arc(R, 0, 0, 0)}`;
        return [`M 0 0 H ${w} ${body} Z`, `M ${w} 0 ${body}`];
    }
  }

  function draw() {
    const w = innerWidth, h = innerHeight;
    if (!w || !h) return;
    svg.setAttribute('viewBox', `0 0 ${w} ${h}`);
    const [f, l] = shape(w, h);
    fill.setAttribute('d', f);
    line.setAttribute('d', l);
  }

  draw();
  addEventListener('resize', draw);
})();
