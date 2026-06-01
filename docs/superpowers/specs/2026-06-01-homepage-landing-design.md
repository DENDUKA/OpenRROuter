# Homepage Landing Design

## Goal

Turn the public home page into a concise landing page for developers and buyers who need an OpenAI-compatible AI model gateway in Russia.

## Audience

- Developers who want to understand the API flow quickly.
- Buyers or operators who need to understand signup, credits, model choice, and key creation before trying the service.

## Page Structure

1. Hero with the primary promise: "OpenAI-compatible API for AI models in Russia".
2. Primary actions: "Signup" and "Explore models".
3. Four-step quickstart: Signup, Buy credits, Choose a model, Get your API key.
4. Model catalog proof section with a link to `/models`.
5. Billing and routing proof cards.
6. OpenAI-compatible request example with a link to `/docs`.
7. Final CTA that repeats the signup and models path.

## Navigation

The top navigation removes the inactive Pricing link. The existing Docs link becomes "Setup guide" and points to `/docs`.

## Setup Guide

The `/docs` route provides a minimal connection guide that mirrors the four-step quickstart and shows the request shape for `/v1/chat/completions`.

## Style

The page keeps the existing dark OpenRouter-like dev-tool style: restrained color, hairline borders, compact cards, mono identifiers, and no heavy decorative hero treatment.

## Constraints

- Do not claim uptime, latency, "best prices", or a fixed model count.
- Use existing routes where possible: `/invite/demo`, `/models`, and `/docs`.
- Keep the page responsive, with the quickstart and proof sections stacking on mobile.
