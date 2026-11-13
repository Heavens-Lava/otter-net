let allServices = [];
let currentCategory = 'all';

async function fetchTelemetry() {
  try {
    const [servicesRes, metricsRes] = await Promise.all([
      fetch('/api/services'),
      fetch('/api/metrics')
    ]);

    if (servicesRes.ok) {
      allServices = await servicesRes.json();
      renderServices();
    }

    if (metricsRes.ok) {
      const metrics = await metricsRes.json();
      renderMetrics(metrics);
    }
  } catch (err) {
    console.error('Failed to fetch telemetry:', err);
  }
}

function renderMetrics(metrics) {
  document.getElementById('metric-services-count').textContent = metrics.totalServices ?? allServices.length;
  document.getElementById('metric-avg-latency').textContent = (metrics.averageLatencyMs ?? 0) + ' ms';
  document.getElementById('metric-global-uptime').textContent = (metrics.overallUptime ?? 99.9) + '%';
}

function renderServices() {
  const container = document.getElementById('services-grid');
  const search = document.getElementById('search-input').value.toLowerCase().trim();

  const filtered = allServices.filter(s => {
    const matchCategory = currentCategory === 'all' || s.category === currentCategory;
    const matchSearch = !search || 
      s.name.toLowerCase().includes(search) || 
      (s.slug && s.slug.toLowerCase().includes(search)) ||
      String(s.port).includes(search);
    return matchCategory && matchSearch;
  });

  if (filtered.length === 0) {
    container.innerHTML = `<div style="grid-column: 1/-1; text-align: center; color: var(--text-muted); padding: 3rem;">No microservices found matching current filters.</div>`;
    return;
  }

  container.innerHTML = filtered.map(s => {
    const isDegraded = s.latencyMs > 120;
    const badgeClass = isDegraded ? 'badge-degraded' : 'badge-healthy';
    const badgeText = isDegraded ? 'Degraded' : 'Healthy';
    const healthPercent = Math.min(100, Math.max(0, s.healthScore || 98));

    return `
      <div class="service-card">
        <div class="service-card-header">
          <div class="service-title-group">
            <span class="service-name">${escapeHtml(s.name)}</span>
            <span class="service-slug">${escapeHtml(s.slug || s.name)} • Port ${s.port}</span>
          </div>
          <span class="service-badge ${badgeClass}">${badgeText}</span>
        </div>
        <div class="service-stats-grid">
          <div class="stat-item">
            <span class="stat-label">Latency</span>
            <span class="stat-value">${s.latencyMs || 25} ms</span>
          </div>
          <div class="stat-item">
            <span class="stat-label">Uptime</span>
            <span class="stat-value">${s.uptime || 99.9}%</span>
          </div>
          <div class="stat-item">
            <span class="stat-label">Category</span>
            <span class="stat-value" style="text-transform: capitalize;">${escapeHtml(s.category)}</span>
          </div>
          <div class="stat-item">
            <span class="stat-label">Health Score</span>
            <span class="stat-value">${healthPercent}/100</span>
          </div>
        </div>
        <div class="service-card-actions">
          <div class="health-bar-container" title="Health: ${healthPercent}%">
            <div class="health-bar-fill" style="width: ${healthPercent}%"></div>
          </div>
          <button class="btn-ping" onclick="pingService('${escapeHtml(s.slug || s.name)}')">Ping Probe</button>
        </div>
      </div>
    `;
  }).join('');
}

async function pingService(slug) {
  try {
    const res = await fetch('/api/services/ping', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ slug: slug })
    });
    if (res.ok) {
      await fetchTelemetry();
    }
  } catch (err) {
    console.error('Failed to ping service:', err);
  }
}

function setCategoryFilter(cat) {
  currentCategory = cat;
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.textContent.toLowerCase().includes(cat === 'edge' ? 'edge' : cat));
  });
  renderServices();
}

function handleSearch() {
  renderServices();
}

function openModal() {
  document.getElementById('register-modal').classList.add('open');
}

function closeModal() {
  document.getElementById('register-modal').classList.remove('open');
}

async function handleRegister(e) {
  e.preventDefault();
  const name = document.getElementById('svc-name').value;
  const port = parseInt(document.getElementById('svc-port').value, 10);
  const category = document.getElementById('svc-category').value;
  const targetUptime = parseFloat(document.getElementById('svc-target-uptime').value);

  try {
    const res = await fetch('/api/services', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, port, category, targetUptime })
    });

    if (res.ok) {
      closeModal();
      document.getElementById('register-form').reset();
      await fetchTelemetry();
    } else {
      alert('Failed to register service: ' + (await res.text()));
    }
  } catch (err) {
    alert('Error connecting to Otter Web Server');
  }
}

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

document.addEventListener('DOMContentLoaded', () => {
  fetchTelemetry();
  setInterval(fetchTelemetry, 5000);
});
