# Provider descriptions

## Goal

Publish concise, factual descriptions in Russian and English for every provider
shown in the production provider directory.

## Scope

The provider cards are Anthropic, Google, MiniMax, OpenAI, Xiaomi MiMo, and
Z.ai. The descriptions describe the provider's model ecosystem rather than the
temporary set of models enabled in OpenRussiaRouter.

## Content structure

Each language version has three short paragraphs:

1. Provider background and its model family.
2. Flagship or representative models and their strengths.
3. Recommended practical use cases.

The texts do not promise availability, pricing, limits, or features that the
gateway has not configured. They identify representative flagship lines only,
and use provider-neutral language where the specific flagship version changes
frequently.

## Publication and verification

Update only `description_ru` and `description_en` in the production
`billing.model_providers` records. Preserve IDs, display names, logos, and all
model and price data. Read the six rows back after the update and verify that
both descriptions are non-empty.
