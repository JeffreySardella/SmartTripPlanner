/**
 * Extracts location data from Yelp business pages.
 * Matches: yelp.com/biz/
 * @returns {{ name?: string, address?: string, category?: string } | null}
 */
function parseYelp() {
  if (!window.location.hostname.includes('yelp.com')) return null;
  if (!window.location.pathname.startsWith('/biz/')) return null;

  const result = {};

  // Business name from h1
  const nameEl = document.querySelector('h1');
  if (nameEl) result.name = nameEl.textContent.trim();

  // Address from the address element or specific Yelp selector
  const addressEl = document.querySelector('address p');
  if (addressEl) result.address = addressEl.textContent.trim();

  // Category from breadcrumbs or category links
  const categoryLinks = document.querySelectorAll('a[href*="/c/"] span');
  if (categoryLinks.length > 0) {
    const cat = categoryLinks[0].textContent.trim().toLowerCase();
    if (cat.includes('restaurant') || cat.includes('food')) result.category = 'restaurant';
    else if (cat.includes('bar') || cat.includes('pub')) result.category = 'bar';
    else if (cat.includes('coffee') || cat.includes('cafe')) result.category = 'cafe';
    else if (cat.includes('hotel') || cat.includes('hostel')) result.category = 'hotel';
    else if (cat.includes('shop') || cat.includes('store')) result.category = 'shop';
    else result.category = 'other';
  }

  return result.name ? result : null;
}
