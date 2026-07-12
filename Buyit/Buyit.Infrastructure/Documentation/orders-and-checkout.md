# Orders & Checkout

Placing an order and what happens to it afterwards.

## Checking out

You place an order by checking out your cart. Checkout needs a complete shipping address: address line,
city, state, postal code, and country. Buyit confirms the items are still in stock, reserves the stock,
applies any coupon, and creates your order. Your cart is emptied once the order is placed.

## Multi-vendor orders and per-store fulfilment

Buyit is a multi-vendor marketplace, so a single order can contain products from several different
stores. Behind the scenes the order is split into a part for each store, and each store fulfils and
ships its own items. That means one order can have items that ship separately by different sellers.

## Order status and tracking

An order moves through stages such as pending, paid, and shipped. You can view your own order history
and the details of any order you placed, including its items, totals, and shipping information. Each
store's part of the order has its own fulfilment and shipping state.

## Payment

Payment is taken for the order total after any discount. Once payment succeeds the order is marked paid
and the sellers are notified to fulfil their parts. If payment fails the order is not confirmed.
