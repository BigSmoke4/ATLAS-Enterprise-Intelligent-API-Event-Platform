/**
 * Client-side table behaviour: sort, filter, paginate over rows the server
 * already rendered. Nothing is invented — filtering hides rows, it does not
 * synthesise them, and an empty result renders an explicit "no matching
 * records" state.
 */

function cellText(row, index) {
  return (row.cells[index]?.textContent ?? '').trim();
}

function compare(a, b) {
  const numberA = Number(a.replace(/[,%]/g, ''));
  const numberB = Number(b.replace(/[,%]/g, ''));
  const bothNumeric = a !== '' && b !== '' && !Number.isNaN(numberA) && !Number.isNaN(numberB);
  if (bothNumeric) return numberA - numberB;
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
}

export function initDataTable(table) {
  if (table.dataset.atlasTableReady === 'true') return;
  table.dataset.atlasTableReady = 'true';

  const tbody = table.tBodies[0];
  if (!tbody) return;

  const panel = table.closest('.atlas-panel') ?? document;
  const filterInput = panel.querySelector('[data-atlas-filter]');
  const pager = panel.querySelector('[data-atlas-pager]');
  const status = pager?.querySelector('[data-atlas-pager-status]');
  const pageSize = Number(table.dataset.pageSize ?? 0);
  const originalRows = Array.from(tbody.querySelectorAll('tr')).filter((row) => !row.classList.contains('atlas-empty-row'));

  let currentPage = 1;
  let sortColumn = null;
  let sortDirection = 'asc';
  let query = '';

  const emptyRow = document.createElement('tr');
  emptyRow.className = 'atlas-empty-row atlas-hidden';
  const emptyCell = document.createElement('td');
  emptyCell.colSpan = table.tHead?.rows[0]?.cells.length ?? 1;
  emptyCell.textContent = 'No matching records.';
  emptyRow.appendChild(emptyCell);
  tbody.appendChild(emptyRow);

  function visibleRows() {
    const filtered = query
      ? originalRows.filter((row) => row.textContent.toLowerCase().includes(query))
      : originalRows.slice();

    if (sortColumn !== null) {
      filtered.sort((a, b) => {
        const result = compare(cellText(a, sortColumn), cellText(b, sortColumn));
        return sortDirection === 'asc' ? result : -result;
      });
    }

    return filtered;
  }

  function render() {
    const rows = visibleRows();
    const total = rows.length;
    const size = pageSize > 0 ? pageSize : total || 1;
    const pageCount = Math.max(1, Math.ceil(total / size));
    currentPage = Math.min(currentPage, pageCount);

    const start = (currentPage - 1) * size;
    const pageRows = rows.slice(start, start + size);

    tbody.querySelectorAll('tr').forEach((row) => {
      if (row !== emptyRow) row.remove();
    });

    pageRows.forEach((row) => tbody.insertBefore(row, emptyRow));
    emptyRow.classList.toggle('atlas-hidden', pageRows.length > 0);

    if (status) {
      status.textContent = total === 0
        ? '0 records'
        : `${start + 1}–${start + pageRows.length} of ${total} record${total === 1 ? '' : 's'}`;
    }

    if (pager) {
      pager.querySelectorAll('[data-atlas-page]').forEach((button) => {
        const direction = button.dataset.atlasPage;
        button.disabled = direction === 'prev' ? currentPage <= 1 : currentPage >= pageCount;
      });
    }

    table.dispatchEvent(new CustomEvent('atlas:table-rendered', { bubbles: true }));
  }

  filterInput?.addEventListener('input', () => {
    query = filterInput.value.trim().toLowerCase();
    currentPage = 1;
    render();
  });

  table.querySelectorAll('th [data-atlas-sort]').forEach((button) => {
    button.addEventListener('click', () => {
      const column = Number(button.dataset.atlasSort);
      if (sortColumn === column) {
        sortDirection = sortDirection === 'asc' ? 'desc' : 'asc';
      } else {
        sortColumn = column;
        sortDirection = 'asc';
      }

      table.querySelectorAll('[data-atlas-sort]').forEach((other) => other.removeAttribute('data-sort-direction'));
      button.setAttribute('data-sort-direction', sortDirection);
      render();
    });
  });

  pager?.querySelectorAll('[data-atlas-page]').forEach((button) => {
    button.addEventListener('click', () => {
      currentPage += button.dataset.atlasPage === 'prev' ? -1 : 1;
      if (currentPage < 1) currentPage = 1;
      render();
    });
  });

  render();
}

export function initDataTables(root = document) {
  root.querySelectorAll('table[data-atlas-table]').forEach(initDataTable);
}
