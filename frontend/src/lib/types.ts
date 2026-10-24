export type ProductCategory = {
  id: number;
  name: string;
};

export type Product = {
  id: number;
  name: string;
  description: string | null;
  price: number;
  stock: number;
  categoryId: number | null;
  category: ProductCategory | null;
  isFeatured: boolean;
  isNewCollection: boolean;
  isBestSeller: boolean;
  imageUrl: string | null;
  videoUrl: string | null;
  imageUrls: string[];
  videoUrls: string[];
};

export type ProductsResult = {
  products: Product[];
  unavailable: boolean;
};

export type ProductResult = {
  product: Product | null;
  unavailable: boolean;
  notFound: boolean;
};
