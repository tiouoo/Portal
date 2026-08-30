export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);

    const response = await env.ASSETS.fetch(request);

    const responseHeaders = new Headers(response.headers);
    responseHeaders.set('X-Content-Type-Options', 'nosniff');
    responseHeaders.set('Referrer-Policy', 'no-referrer');

    if (/\.(?:woff2?|ttf|otf)$/i.test(url.pathname)) {
      responseHeaders.set('Cache-Control', 'public, max-age=31536000, immutable');
    } else {
      responseHeaders.set('Cache-Control', 'no-store');
    }

    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers: responseHeaders,
    });
  },
};
