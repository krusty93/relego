import { defineCollection, z } from 'astro:content';
import { docsLoader } from '@astrojs/starlight/loaders';
import { docsSchema } from '@astrojs/starlight/schema';

export const collections = {
	docs: defineCollection({
		loader: docsLoader(),
		schema: docsSchema({
			extend: z.object({
				// Which stop on the round trip this page is, so the circuit stays
				// visible on the pages readers actually spend their time on.
				stage: z.number().int().min(1).max(3).optional(),
				eyebrow: z.string().optional(),
			}),
		}),
	}),
};
